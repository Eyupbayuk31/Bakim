"""
Tasarım ve kod borcu cırcırı (ratchet) — MASTER_PLAN §3.9.

Borç sayıları Tools/design-debt-baseline.json'daki eşiği AŞAMAZ; azaldıkça eşik
`--update` ile düşürülür. Böylece göç kademeli ilerler ama geri kayma olmaz.

Ölçülenler:
  xaml_hex_colors     Views/ ve Controls/ XAML'inde sabit renk (#RGB, #RRGGBB, #AARRGGBB)
  xaml_font_sizes     Sabit metin FontSize'ı (simgeler hariç) — Font.* / Text.* kullanın
  xaml_corner_radius  Sabit CornerRadius — Radius.* kullanın
  code_hex_colors     Models/, ViewModels/, Services/ içinde "#RRGGBB" dize sabiti (ThemeService hariç)
  empty_catch         Boş `catch { }` blokları (hatayı sessizce yutar)
  danger_in_pages     Sayfalarda (Views/Modules) kırmızı düğme — Fluent: yalnızca onay diyaloğunda
  non_fluent_button   Success / Caution / Info / Light / Dark düğme görünümü (Fluent'te yok)
  infinite_animation  RepeatBehavior="Forever" (dekoratif döngü; ProgressRing/Bar hariç)
  gradient_brush      Linear/RadialGradientBrush (Mica dışında gradyan yok)
  title_case_text     Her sözcüğü Büyük Harfli kısa sabit metin (Windows 11: cümle düzeni)
  ampersand_text      Görünen metinde "&" (Türkçede "ve")

Çalıştırma:  python Tools/verify-design-debt.py [--update]
Çıkış kodu:  0 = eşik aşılmadı, 1 = borç arttı
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
BASELINE = ROOT / "Tools" / "design-debt-baseline.json"
EXCLUDE = {"bin", "obj", ".git", ".vs", "Tests", "Tools", "ui-ux-pro-max-skill", ".agents"}

ICON_TAG = re.compile(r"<(ui:SymbolIcon|ui:FontIcon|ui:ProgressRing|ui:ImageIcon)\b[^>]*>", re.S)
HEX_ATTR = re.compile(r'="#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})"')
HEX_ELEMENT = re.compile(r'>\s*#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\s*<')
FONT_ATTR = re.compile(r'\bFontSize="[0-9]+(?:\.[0-9]+)?"')
FONT_SETTER = re.compile(r'<Setter\s+Property="FontSize"\s+Value="[0-9]')
CORNER_ATTR = re.compile(r'\bCornerRadius="[0-9]')
CORNER_SETTER = re.compile(r'<Setter\s+Property="CornerRadius"\s+Value="[0-9]')
CODE_HEX = re.compile(r'"#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})"')
EMPTY_CATCH = re.compile(r"catch(?:\s*\([^)]*\))?\s*\{\s*\}")


def iter_files(roots, pattern):
    for root in roots:
        base = ROOT / root
        if base.is_file():
            yield base
            continue
        if not base.exists():
            continue
        for p in base.rglob(pattern):
            if not any(part in EXCLUDE for part in p.relative_to(ROOT).parts):
                yield p


def measure():
    counts = {k: 0 for k in ("xaml_hex_colors", "xaml_font_sizes", "xaml_corner_radius", "code_hex_colors", "empty_catch",
                                   "danger_in_pages", "non_fluent_button", "infinite_animation", "gradient_brush",
                                   "title_case_text", "ampersand_text")}
    per_file = {k: {} for k in counts}

    def bump(key, path, n):
        if n:
            counts[key] += n
            rel = path.relative_to(ROOT).as_posix()
            per_file[key][rel] = per_file[key].get(rel, 0) + n

    for path in iter_files(["Views", "Controls", "MainWindow.xaml", "LoginWindow.xaml"], "*.xaml"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        bump("xaml_hex_colors", path, len(HEX_ATTR.findall(text)) + len(HEX_ELEMENT.findall(text)))
        no_icons = ICON_TAG.sub("", text)
        bump("xaml_font_sizes", path, len(FONT_ATTR.findall(no_icons)) + len(FONT_SETTER.findall(text)))
        bump("xaml_corner_radius", path, len(CORNER_ATTR.findall(text)) + len(CORNER_SETTER.findall(text)))

    for path in iter_files(["Views", "Controls", "MainWindow.xaml", "LoginWindow.xaml"], "*.xaml"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        if "Views/Modules" in path.as_posix() or "Views\\Modules" in str(path):
            bump("danger_in_pages", path, len(re.findall(r'Appearance="Danger"', text)))
        bump("non_fluent_button", path, len(re.findall(r'Appearance="(?:Success|Caution|Info|Light|Dark)"', text)))
        bump("infinite_animation", path, len(re.findall(r'RepeatBehavior="Forever"', text)))
        bump("gradient_brush", path, len(re.findall(r"<(?:Linear|Radial)GradientBrush\b", text)))
        texts = re.findall(r'(?<![\w.:])(?:Text|Content|Title|Header|Label|Description)="([^"{][^"]*)"', text)
        def title_case(t):
            words = [w for w in t.replace("&amp;", " ").split() if w[:1].isalpha()]
            return len(words) >= 2 and all(w[0].isupper() for w in words) and "\\" not in t
        bump("title_case_text", path, sum(1 for t in texts if title_case(t)))
        bump("ampersand_text", path, sum(1 for t in texts if " &amp; " in t))

    for path in iter_files(["Models", "ViewModels", "Services"], "*.cs"):
        # ThemeService paletlerin tek resmi kaynağıdır; oradaki renk sabitleri borç değil tanımdır.
        if path.name == "ThemeService.cs":
            continue
        text = path.read_text(encoding="utf-8", errors="ignore")
        bump("code_hex_colors", path, len(CODE_HEX.findall(text)))

    for path in iter_files(["."], "*.cs"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        bump("empty_catch", path, len(EMPTY_CATCH.findall(text)))

    return counts, per_file


def main():
    counts, per_file = measure()
    baseline = json.loads(BASELINE.read_text(encoding="utf-8")) if BASELINE.exists() else {}

    if "--update" in sys.argv:
        BASELINE.write_text(json.dumps(counts, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print("Esikler guncellendi:", json.dumps(counts))
        return 0

    failed = False
    for key, value in counts.items():
        limit = baseline.get(key)
        if limit is None:
            print(f"  {key:20} {value:6}  (esik yok — `--update` ile olusturun)")
            continue
        status = "OK" if value <= limit else "ARTTI"
        print(f"  {key:20} {value:6} / {limit:<6} {status}")
        if value > limit:
            failed = True
            top = sorted(per_file[key].items(), key=lambda kv: -kv[1])[:10]
            for rel, n in top:
                print(f"      {n:4}  {rel}")
        elif value < limit:
            print(f"      (azaldi: esigi dusurmek icin `python Tools/verify-design-debt.py --update`)")

    if failed:
        print("\nHATA: Tasarim/kod borcu artti. Token ve stilleri kullanin ya da hatayi gunluge yazin.")
        return 1
    print("\n[OK] Borc esiklerin altinda.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
