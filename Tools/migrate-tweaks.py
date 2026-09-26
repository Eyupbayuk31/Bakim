#!/usr/bin/env python3
"""
Basit kayıt defteri ince ayarlarını servis kodundan Assets/tweaks/*.json kataloğuna taşır
(MASTER_PLAN §5.16). Yalnızca davranışı BİREBİR yeniden üretilebilen ayarlar taşınır:

  * tanım liste başlatıcısının doğrudan elemanı (koşullu list.Add değil), Type Toggle,
  * algılama CheckRegistryDword / CheckRegistryString / CheckKeyExists / !CheckKeyExists /
    CheckRegistryValueExists çağrısı ve bu kontrol "açma" değişikliklerinde karşılık buluyor,
  * uygulama gövdesi yalnızca SetRegistryDword / SetRegistryString / DeleteRegistryValue /
    DeleteRegistryKey / CreateSubKey ve `if (enable) {..} else {..}` içeriyor.

Diğer her şey (komutlar, hizmetler, yerel değişkenler, özel algılama) servis kodunda kalır.

Kullanım:  python Tools/migrate-tweaks.py [--dry-run]
"""
import json
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent
SERVICES = [
    "BehaviorTweaksService", "BootLogonTweaksService", "DesktopTaskbarTweaksService",
    "EdgeTweaksService", "FileExplorerTweaksService", "SettingsControlPanelTweaksService",
]
ROOTS = {"Registry.CurrentUser": "HKCU", "Registry.LocalMachine": "HKLM"}


# ---------------------------------------------------------------- C# sözcük düzeyi yardımcılar

def skip_string(s, i):
    """s[i] bir dize başlangıcıysa sonrasının indeksini döndürür, değilse None."""
    if s.startswith('@"', i):
        j = i + 2
        while j < len(s):
            if s[j] == '"':
                if j + 1 < len(s) and s[j + 1] == '"':
                    j += 2
                    continue
                return j + 1
            j += 1
        raise ValueError("kapanmayan verbatim dize")
    if s[i] == '"':
        j = i + 1
        while j < len(s):
            if s[j] == '\\':
                j += 2
                continue
            if s[j] == '"':
                return j + 1
            j += 1
        raise ValueError("kapanmayan dize")
    if s[i] == "'":
        j = i + 1
        while j < len(s):
            if s[j] == '\\':
                j += 2
                continue
            if s[j] == "'":
                return j + 1
            j += 1
    return None


def skip_comment(s, i):
    if s.startswith("//", i):
        j = s.find("\n", i)
        return len(s) if j < 0 else j
    if s.startswith("/*", i):
        return s.index("*/", i) + 2
    return None


def match_brace(s, i, open_ch="{", close_ch="}"):
    """s[i] == open_ch; eşleşen kapanışın indeksini döndürür."""
    depth = 0
    j = i
    while j < len(s):
        k = skip_string(s, j) or skip_comment(s, j)
        if k:
            j = k
            continue
        if s[j] == open_ch:
            depth += 1
        elif s[j] == close_ch:
            depth -= 1
            if depth == 0:
                return j
        j += 1
    raise ValueError("eşleşmeyen parantez")


def split_top(s, sep=","):
    """Parantez/dize dışındaki ayırıcılara göre böler."""
    parts, depth, start, j = [], 0, 0, 0
    while j < len(s):
        k = skip_string(s, j) or skip_comment(s, j)
        if k:
            j = k
            continue
        c = s[j]
        if c in "({[":
            depth += 1
        elif c in ")}]":
            depth -= 1
        elif c == sep and depth == 0:
            parts.append(s[start:j])
            start = j + 1
        j += 1
    parts.append(s[start:])
    return [p.strip() for p in parts if p.strip()]


def cs_string(expr):
    expr = expr.strip()
    if expr.startswith('@"') and expr.endswith('"'):
        return expr[2:-1].replace('""', '"')
    if expr.startswith('"') and expr.endswith('"'):
        body = expr[1:-1]
        out, j = [], 0
        while j < len(body):
            if body[j] == "\\" and j + 1 < len(body):
                n = body[j + 1]
                out.append({"n": "\n", "t": "\t", "\\": "\\", '"': '"', "'": "'", "0": "\0"}.get(n, n))
                j += 2
            else:
                out.append(body[j])
                j += 1
        return "".join(out)
    return None


def cs_int(expr):
    expr = expr.strip()
    if re.fullmatch(r"-?\d+", expr):
        return int(expr)
    if re.fullmatch(r"0x[0-9A-Fa-f]+", expr):
        return int(expr, 16)
    return None


def ternary(expr):
    """'enable ? a : b' → (a, b); '!enable ? a : b' → (b, a)."""
    m = re.fullmatch(r"\s*(!?)enable\s*\?\s*(.+?)\s*:\s*(.+?)\s*", expr, re.S)
    if not m:
        return None
    a, b = m.group(2), m.group(3)
    return (b, a) if m.group(1) else (a, b)


# ---------------------------------------------------------------- tanım ve gövde ayrıştırma

def parse_call(stmt):
    m = re.fullmatch(r"([\w.]+)\s*\((.*)\)\s*;?", stmt.strip(), re.S)
    if not m:
        return None, None
    return m.group(1), split_top(m.group(2))


def eval_value(expr, enable, kind):
    t = ternary(expr)
    if t:
        expr = t[0] if enable else t[1]
    return cs_int(expr) if kind == "int" else cs_string(expr)


def eval_statements(body, enable):
    """Gövdeyi enable için sembolik çalıştırır; desteklenmeyen deyimde None döner."""
    ops = []
    i = 0
    s = body
    while i < len(s):
        k = skip_comment(s, i)
        if k:
            i = k
            continue
        if s[i].isspace():
            i += 1
            continue
        if s.startswith("break;", i):
            return ops
        m = re.match(r"if\s*\(\s*(!?)enable\s*\)\s*\{", s[i:])
        if m:
            open_i = i + m.end() - 1
            close_i = match_brace(s, open_i)
            then_body = s[open_i + 1:close_i]
            j = close_i + 1
            else_body = ""
            m2 = re.match(r"\s*else\s*\{", s[j:])
            if m2:
                open2 = j + m2.end() - 1
                close2 = match_brace(s, open2)
                else_body = s[open2 + 1:close2]
                j = close2 + 1
            cond = enable if not m.group(1) else not enable
            inner = eval_statements(then_body if cond else else_body, enable)
            if inner is None:
                return None
            ops.extend(inner)
            i = j
            continue
        # tek deyim: ';' ye kadar
        end = i
        while end < len(s):
            k2 = skip_string(s, end)
            if k2:
                end = k2
                continue
            if s[end] == ";":
                break
            end += 1
        stmt = s[i:end + 1].strip()
        i = end + 1
        op = parse_op(stmt, enable)
        if op == "skip":
            continue
        if op is None:
            return None
        ops.append(op)
    return ops


def parse_op(stmt, enable):
    m = re.fullmatch(r"using\s+var\s+\w+\s*=\s*(Registry\.\w+)\.CreateSubKey\((.+?)(?:,\s*true)?\)\s*;", stmt, re.S)
    if m:
        root = ROOTS.get(m.group(1))
        path = cs_string(m.group(2))
        return {"op": "CreateKey", "root": root, "key": path} if root and path else None
    name, args = parse_call(stmt)
    if name is None:
        return None
    if name.endswith("RegistryCapture.TrackKey"):
        return "skip"
    if name == "SetRegistryDword" and len(args) == 4:
        root, path, vname = ROOTS.get(args[0]), cs_string(args[1]), cs_string(args[2])
        value = eval_value(args[3], enable, "int")
        if root and path is not None and vname is not None and value is not None:
            return {"op": "SetDword", "root": root, "key": path, "name": vname, "dword": value}
    if name == "SetRegistryString" and len(args) == 4:
        root, path, vname = ROOTS.get(args[0]), cs_string(args[1]), cs_string(args[2])
        value = eval_value(args[3], enable, "str")
        if root and path is not None and vname is not None and value is not None:
            return {"op": "SetString", "root": root, "key": path, "name": vname, "text": value}
    if name == "DeleteRegistryValue" and len(args) == 3:
        root, path, vname = ROOTS.get(args[0]), cs_string(args[1]), cs_string(args[2])
        if root and path is not None and vname is not None:
            return {"op": "DeleteValue", "root": root, "key": path, "name": vname}
    if name == "DeleteRegistryKey" and len(args) == 2:
        root, path = ROOTS.get(args[0]), cs_string(args[1])
        if root and path is not None:
            return {"op": "DeleteKey", "root": root, "key": path}
    return None


def same(a, b):
    return a.lower() == b.lower()


def detection_matches(expr, on_ops):
    """Eski algılama ifadesi 'açma' değişikliklerinde karşılık buluyor mu?"""
    expr = expr.strip()
    neg = expr.startswith("!")
    name, args = parse_call(expr.lstrip("!"))
    if name is None:
        return False
    root = ROOTS.get(args[0]) if args else None
    path = cs_string(args[1]) if len(args) > 1 else None
    if not root or path is None:
        return False

    def find(pred):
        return any(o["root"] == root and same(o["key"], path) and pred(o) for o in on_ops)

    if name == "CheckRegistryDword" and not neg and len(args) == 4:
        v = cs_int(args[3])
        return find(lambda o: o["op"] == "SetDword" and same(o["name"], cs_string(args[2]) or "") and o["dword"] == v)
    if name == "CheckRegistryString" and not neg and len(args) == 4:
        v = cs_string(args[3])
        return find(lambda o: o["op"] == "SetString" and same(o["name"], cs_string(args[2]) or "") and v is not None and same(o["text"], v))
    if name == "CheckRegistryValueExists" and not neg and len(args) == 3:
        return find(lambda o: o["op"] in ("SetDword", "SetString") and same(o["name"], cs_string(args[2]) or ""))
    if name == "CheckKeyExists" and len(args) == 2:
        if neg:
            return find(lambda o: o["op"] == "DeleteKey")
        return find(lambda o: o["op"] in ("CreateKey", "SetDword", "SetString"))
    return False


# ---------------------------------------------------------------- servis dosyası

def parse_entries(src):
    start = src.index("var list = new List<SystemTweakItem>")
    open_i = src.index("{", start)
    close_i = match_brace(src, open_i)
    entries = []
    j = open_i + 1
    while j < close_i:
        k = skip_comment(src, j)
        if k:
            j = k
            continue
        m = re.match(r"new(?:\s+SystemTweakItem)?\s*\(\s*\)\s*\{|new\s+SystemTweakItem\s*\{", src[j:])
        if m:
            b_open = j + m.end() - 1
            b_close = match_brace(src, b_open)
            # önceki yorum satırları + sonraki virgül ile birlikte silinecek aralık
            line_start = src.rfind("\n", 0, j) + 1
            rm_start = line_start
            while True:
                prev_end = rm_start - 1
                prev_start = src.rfind("\n", 0, prev_end) + 1
                if prev_start >= open_i + 1 and src[prev_start:prev_end].strip().startswith("//"):
                    rm_start = prev_start
                else:
                    break
            rm_end = b_close + 1
            m2 = re.match(r"\s*,", src[rm_end:])
            if m2:
                rm_end += m2.end()
            props = {}
            for part in split_top(src[b_open + 1:b_close]):
                pm = re.match(r"(\w+)\s*=\s*(.+)", part, re.S)
                if pm:
                    props[pm.group(1)] = pm.group(2).strip()
            entries.append({"props": props, "remove": (rm_start, rm_end)})
            j = rm_end
            continue
        j += 1
    return entries, (open_i, close_i)


def parse_cases(src):
    sw = src.index("switch (tweak.Id)")
    open_i = src.index("{", sw)
    close_i = match_brace(src, open_i)
    body = src[open_i + 1:close_i]
    cases = {}
    for m in re.finditer(r'case\s+"([^"]+)"\s*:', body):
        start = m.end()
        # gövde: sonraki "case"/"default" etiketine kadar (break; dahil)
        nxt = re.search(r'\n\s*(case\s+"|default\s*:)', body[start:])
        end = start + (nxt.start() if nxt else len(body))
        # Sonraki case'in önündeki yorum satırları o case'e aittir: gövdeden çıkarılır
        # (aksi halde iki silme aralığı çakışır).
        lines = body[start:end].split("\n")
        while lines and (not lines[-1].strip() or lines[-1].strip().startswith("//")):
            lines.pop()
        end = start + len("\n".join(lines))
        seg = body[start:end]
        # önceki yorum satırlarını da sil
        line_start = body.rfind("\n", 0, m.start()) + 1
        rm_start = line_start
        while True:
            prev_end = rm_start - 1
            prev_start = body.rfind("\n", 0, prev_end) + 1
            if prev_end > 0 and body[prev_start:prev_end].strip().startswith("//"):
                rm_start = prev_start
            else:
                break
        # birden çok etiketli case'ler (case "a": case "b":) desteklenmez
        if re.match(r"\s*case\s", seg):
            continue
        cases[m.group(1)] = {"body": seg, "remove": (open_i + 1 + rm_start, open_i + 1 + end)}
    return cases


def migrate(service, dry):
    path = ROOT / "Services" / f"{service}.cs"
    src = path.read_text(encoding="utf-8")
    entries, _ = parse_entries(src)
    cases = parse_cases(src)

    migrated, removals, categories = [], [], []
    for e in entries:
        p = e["props"]
        tid = cs_string(p.get("Id", "")) or ""
        if not tid or tid not in cases:
            continue
        if p.get("Type", "TweakType.Toggle") != "TweakType.Toggle":
            continue
        body = cases[tid]["body"]
        on, off = eval_statements(body, True), eval_statements(body, False)
        if not on or not off:
            continue
        if not detection_matches(p.get("IsEnabled", ""), on):
            continue
        category = cs_string(p.get("Category", "")) or ""
        definition = {
            "id": tid,
            "category": category,
            "title": cs_string(p.get("Title", "")) or tid,
            "description": cs_string(p.get("Description", "")) or "",
            "icon": cs_string(p.get("IconSymbol", '"Wrench24"')) or "Wrench24",
            "recommended": p.get("IsRecommended", "false") == "true",
            "requiresRestart": p.get("RequiresRestart", "false") == "true",
            "on": on,
            "off": off,
        }
        migrated.append(definition)
        if category not in categories:
            categories.append(category)
        removals.append(e["remove"])
        removals.append(cases[tid]["remove"])

    print(f"{service}: {len(migrated)}/{len(entries)} ayar taşındı")
    if dry or not migrated:
        return migrated

    ordered = sorted(removals)
    for (a0, a1), (b0, b1) in zip(ordered, ordered[1:]):
        if b0 < a1:
            raise SystemExit(f"{service}: çakışan silme aralıkları ({a0}-{a1}, {b0}-{b1})")
    for start, end in sorted(removals, reverse=True):
        src = src[:start] + src[end:]

    # Katalog ayarları listenin başına eklenir; uygulama katalog motoruna devredilir.
    get_anchor = src.index("var list = new List<SystemTweakItem>")
    ret = src.index("return list;", get_anchor)
    indent = src[src.rfind("\n", 0, ret) + 1:ret]
    inserts = "".join(
        f'{indent}list.InsertRange(0, Bakım.Services.Tweaks.TweakEngine.ItemsFor({json.dumps(c, ensure_ascii=False)}));\n'
        for c in reversed(categories))
    src = src[:ret] + inserts.lstrip() + indent + src[ret:]

    apply_sig = re.search(r"public async Task<bool> ApplyTweakAsync\(SystemTweakItem tweak, bool enable\)\s*\{", src)
    at = apply_sig.end()
    src = src[:at] + (
        "\n            // Veri tabanlı ayarlar (Assets/tweaks/*.json) tek motordan uygulanır (MASTER_PLAN §5.16)."
        "\n            if (Bakım.Services.Tweaks.TweakEngine.Handles(tweak.Id))"
        "\n                return await Bakım.Services.Tweaks.TweakEngine.ApplyAsync(tweak, enable);\n"
    ) + src[at:]
    path.write_text(src, encoding="utf-8")
    return migrated


def main():
    dry = "--dry-run" in sys.argv
    out_dir = ROOT / "Assets" / "tweaks"
    total = 0
    for service in SERVICES:
        items = migrate(service, dry)
        total += len(items)
        if items and not dry:
            out_dir.mkdir(parents=True, exist_ok=True)
            name = re.sub(r"TweaksService$", "", service)
            name = re.sub(r"(?<!^)(?=[A-Z])", "-", name).lower()
            (out_dir / f"{name}.json").write_text(json.dumps(items, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Toplam {total} ayar {'taşınabilir' if dry else 'taşındı'}.")


if __name__ == "__main__":
    main()
