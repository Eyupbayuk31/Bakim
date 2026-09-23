import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
SYMBOL_FILE = ROOT / "Tools" / "SymbolRegular.cs"

if not SYMBOL_FILE.exists():
    print("HATA: Tools/SymbolRegular.cs bulunamadi!")
    sys.exit(1)

# Extract valid symbols
symbol_pattern = re.compile(r"^\s*([A-Za-z0-9]+)\s*=\s*0x[0-9A-Fa-f]+,", re.MULTILINE)
valid_symbols = set(symbol_pattern.findall(SYMBOL_FILE.read_text(encoding="utf-8", errors="ignore")))
print(f"Toplam tanimli SymbolRegular sembolu: {len(valid_symbols)}")

# Scan all XAML and CS files
xaml_files = [p for p in ROOT.rglob("*.xaml") if not any(x in p.parts for x in ["bin", "obj", ".git"])]
cs_files = [p for p in ROOT.rglob("*.cs") if not any(x in p.parts for x in ["bin", "obj", ".git", "Tools"])]

bad_items = []

# Check XAML files
ui_symbol_pattern = re.compile(r"\{ui:SymbolIcon\s+([A-Za-z0-9]+)\}")
symbol_prop_pattern = re.compile(r'Symbol="([A-Za-z0-9]+)"')

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
        print(f"  [X] {file}: '{sym}' -> {context}")
    sys.exit(1)
else:
    print("\n[OK] Tum XAML ve C# dosyalarindaki semboller 100% gecerli!")
    sys.exit(0)
