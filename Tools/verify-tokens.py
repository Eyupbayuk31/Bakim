"""
Tasarım sistemi bütünlük denetimi.

Eksik bir kaynak anahtarı WPF'te İSTİSNA FIRLATMAZ — öğe sessizce görünmez olur.
Bu yüzden derleme yeşil olsa bile migrasyonun doğruluğu ayrıca kanıtlanmalıdır.

Denetimler:
  1. XAML'de kullanılan her tasarım sistemi anahtarı gerçekten tanımlı mı?
  2. Tanımlı ama hiç kullanılmayan anahtar var mı? (ölü token)
  3. ThemeService ile açılış paleti arasında anahtar kayması var mı?

Çalıştırma:  python verify-tokens.py
Çıkış kodu:  0 = temiz, 1 = eksik anahtar var
"""
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent  # depo koku
EXCLUDE_DIRS = {"bin", "obj", ".git", ".vs", "ui-ux-pro-max-skill", ".agents"}
TOKENS_DIR = ROOT / "Themes" / "Tokens"
# Paylaşılan denetim stilleri (Nav.ItemButton, Card.Setting ...) de tanım kaynağıdır.
STYLES_DIR = ROOT / "Themes" / "Controls"

# Bize ait sayılan önekler; diğerleri WPF-UI'dan gelir ve denetlenmez.
OWNED_PREFIXES = (
    "Brush.", "Color.", "Font.", "Icon.", "Space.", "Pad.",
    "Gap.", "Radius.", "Stroke.", "Duration.", "Ease.", "Time.", "Text.",
    "Surface.", "Border.", "Status.", "Risk.", "Chart.", "Motion.",
    "Layout.", "Card.", "Nav.",
)

KEY_USE = re.compile(r'\{(?:Dynamic|Static)Resource\s+([A-Za-z0-9_.]+)\s*\}')
KEY_DEF = re.compile(r'x:Key="([A-Za-z0-9_.]+)"')
THEME_SET = re.compile(r'Set(?:Color)?\(\s*res\s*,\s*"([A-Za-z0-9_.]+)"')
# SemanticV2: yield return ("Surface.Base", ...) ve $"Status.{name}.Solid" kalıpları
THEME_YIELD = re.compile(r'yield return \(\s*"([A-Za-z0-9_.]+)"')


def iter_xaml(include_tokens):
    for path in ROOT.rglob("*.xaml"):
        if any(part in EXCLUDE_DIRS for part in path.parts):
            continue
        in_tokens = TOKENS_DIR in path.parents or STYLES_DIR in path.parents
        if in_tokens and not include_tokens:
            continue
        if not in_tokens and include_tokens:
            continue
        yield path


def bootstrap_keys_preview():
    path = TOKENS_DIR / "Palette.Bootstrap.xaml"
    return set(KEY_DEF.findall(path.read_text(encoding="utf-8"))) if path.exists() else set()


def main():
    # --- Tanımlı anahtarlar ---
    bootstrap_keys = set()
    static_token_keys = set()

    for path in iter_xaml(include_tokens=True):
        keys = set(KEY_DEF.findall(path.read_text(encoding="utf-8")))
        if path.name == "Palette.Bootstrap.xaml":
            bootstrap_keys |= keys
        else:
            static_token_keys |= keys

    theme_src = (ROOT / "Services" / "ThemeService.cs").read_text(encoding="utf-8")
    theme_keys = set(THEME_SET.findall(theme_src)) | set(THEME_YIELD.findall(theme_src))
    # Şablonlu anahtarlar (Status.{name}.Solid, Risk.{name}, Chart.Series{i}) açılış paletinden doğrulanır:
    theme_keys |= {k for k in bootstrap_keys_preview() if k.startswith(("Status.", "Risk.", "Chart."))}

    app_keys = set(KEY_DEF.findall((ROOT / "App.xaml").read_text(encoding="utf-8")))

    defined = bootstrap_keys | static_token_keys | theme_keys | app_keys

    # --- Kullanılan anahtarlar ---
    used = {}
    for path in iter_xaml(include_tokens=False):
        for key in KEY_USE.findall(path.read_text(encoding="utf-8")):
            used.setdefault(key, set()).add(path.relative_to(ROOT).as_posix())

    owned_used = {k: v for k, v in used.items() if k.startswith(OWNED_PREFIXES)}

    # --- 1. Eksik anahtarlar ---
    missing = {k: v for k, v in owned_used.items() if k not in defined}

    print(f"Tanimli anahtar     : {len(defined)}")
    print(f"  - acilis paleti   : {len(bootstrap_keys)}")
    print(f"  - ThemeService    : {len(theme_keys)}")
    print(f"  - statik tokenlar : {len(static_token_keys)}")
    print(f"XAML'de kullanilan  : {len(used)} ({len(owned_used)} tanesi bize ait)")
    print()

    if missing:
        print("EKSIK ANAHTARLAR (sessiz gorunmezlik riski):")
        for key, files in sorted(missing.items()):
            print(f"  {key}")
            for f in sorted(files):
                print(f"      {f}")
    else:
        print("[OK] Kullanilan her tasarim sistemi anahtari tanimli.")

    # --- 2. Tema anahtarı kayması ---
    runtime_only = theme_keys - bootstrap_keys
    bootstrap_only = bootstrap_keys - theme_keys

    print()
    if runtime_only:
        print("UYARI: ThemeService yayinliyor ama acilis paletinde yok")
        print("       (tasarimci onizlemesinde bos gorunur):")
        for k in sorted(runtime_only):
            print(f"  {k}")
    if bootstrap_only:
        print("UYARI: Acilis paletinde var ama ThemeService yayinlamiyor")
        print("       (tema degisince GUNCELLENMEZ):")
        for k in sorted(bootstrap_only):
            print(f"  {k}")
    if not runtime_only and not bootstrap_only:
        print("[OK] ThemeService ile acilis paleti birebir ortusuyor.")

    # --- 3. Ölü tokenlar ---
    dead = sorted(k for k in static_token_keys if k not in used)
    if dead:
        print()
        print(f"Bilgi: {len(dead)} statik token henuz kullanilmiyor "
              f"(yeni olcek, migrasyon ilerledikce tuketilecek).")

    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
