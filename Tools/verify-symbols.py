import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent
SYMBOL_FILE = ROOT / "Tools" / "SymbolRegular.txt"

if not SYMBOL_FILE.exists():
    print("HATA: Tools/SymbolRegular.txt bulunamadi!")
    sys.exit(1)

# Extract valid symbols
symbol_pattern = re.compile(r"^\s*([A-Za-z0-9]+)\s*=\s*0x([0-9A-Fa-f]+),", re.MULTILINE)
symbol_codes = {name: int(code, 16) for name, code in
                symbol_pattern.findall(SYMBOL_FILE.read_text(encoding="utf-8", errors="ignore"))}

# WPF-UI 4.x SymbolExtensions.GetString kod noktasini UTF-16'ya dogrudan iki karaktere boler
# (vekil cift uretmez). 0xFFFF'in ustundeki semboller bu yuzden bozuk glif olarak cizilir:
# ornegin HardDrive24 = 0xF0306 -> U+0306 (birlesik "˘") + kontrol karakteri.
# Bu semboller "tanimli" sayilmaz; ayni simgenin 0xFFFF altindaki boyutu kullanilmalidir.
valid_symbols = {name for name, code in symbol_codes.items() if code <= 0xFFFF}
print(f"Toplam kullanilabilir SymbolRegular sembolu: {len(valid_symbols)} / {len(symbol_codes)}")

# Scan all XAML and CS files
xaml_files = [p for p in ROOT.rglob("*.xaml") if not any(x in p.parts for x in ["bin", "obj", ".git"])]
cs_files = [p for p in ROOT.rglob("*.cs") if not any(x in p.parts for x in ["bin", "obj", ".git", "Tools"])]

bad_items = []

# Check XAML files
ui_symbol_pattern = re.compile(r"\{ui:SymbolIcon\s+([A-Za-z0-9]+)\}")
symbol_prop_pattern = re.compile(r'Symbol="([A-Za-z0-9]+)"')
# SectionHeader / StatusBadge / StatCard gibi bilesenlerin dize Icon ozelligi (SafeSymbol ile cozulur)
icon_prop_pattern = re.compile(r'\bIcon="([A-Za-z]+[0-9]+)"')

for xaml in xaml_files:
    content = xaml.read_text(encoding="utf-8", errors="ignore")
    for m in ui_symbol_pattern.finditer(content):
        sym = m.group(1)
        if sym not in valid_symbols:
            bad_items.append((xaml.relative_to(ROOT), sym, f"{{ui:SymbolIcon {sym}}}"))
    for m in symbol_prop_pattern.finditer(content):
        sym = m.group(1)
        if sym not in valid_symbols:
            bad_items.append((xaml.relative_to(ROOT), sym, f'Symbol="{sym}"'))
    for m in icon_prop_pattern.finditer(content):
        sym = m.group(1)
        if sym not in valid_symbols:
            bad_items.append((xaml.relative_to(ROOT), sym, f'Icon="{sym}"'))

# Check CS files for SymbolRegular.XYZ or IconSymbol = "XYZ"
cs_symbol_pattern = re.compile(r"SymbolRegular\.([A-Za-z0-9]+)")
cs_icon_pattern = re.compile(r'IconSymbol\s*=\s*"([A-Za-z0-9]+)"')

for cs in cs_files:
    content = cs.read_text(encoding="utf-8", errors="ignore")
    for m in cs_symbol_pattern.finditer(content):
        sym = m.group(1)
        if sym not in valid_symbols:
            bad_items.append((cs.relative_to(ROOT), sym, f"SymbolRegular.{sym}"))
    for m in cs_icon_pattern.finditer(content):
        sym = m.group(1)
        if sym not in valid_symbols:
            bad_items.append((cs.relative_to(ROOT), sym, f'IconSymbol = "{sym}"'))

if bad_items:
    print(f"\nHATA: {len(bad_items)} adet gecersiz SymbolRegular tespit edildi:")
    for file, sym, context in bad_items:
        reason = " (0xFFFF ustu: WPF-UI bozuk cizer)" if sym in symbol_codes else ""
        print(f"  [X] {file}: '{sym}' -> {context}{reason}")
    sys.exit(1)
else:
    print("\n[OK] Tum XAML ve C# dosyalarindaki semboller 100% gecerli!")
    sys.exit(0)
