"""
Sabit CornerRadius değerlerini yarıçap token'larına taşır (MASTER_PLAN §3.3).

  <=4 -> Radius.XS (4)   5-6 -> Radius.SM (6)   7-9 -> Radius.MD (8)
  10-12 -> Radius.LG (12)   13-16 -> Radius.XL (16)
  "6,6,0,0" -> Radius.TopOnly   "0,0,6,6" -> Radius.BottomOnly
  >=20 (daire/elips şekiller) ve diğer karışık değerler olduğu gibi kalır.

Çalıştırma:  python Tools/migrate-radius.py
"""
import re
from pathlib import Path
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent
SCAN = [ROOT / "Views", ROOT / "Controls", ROOT / "MainWindow.xaml", ROOT / "LoginWindow.xaml"]
ATTR = re.compile(r'\bCornerRadius="(?P<v>[0-9., ]+)"')


def token_for(raw: str):
    raw = raw.strip()
    if raw == "6,6,0,0":
        return "Radius.TopOnly"
    if raw == "0,0,6,6":
        return "Radius.BottomOnly"
    try:
        v = float(raw)
    except ValueError:
        return None
    if v <= 0:
        return "Radius.None"
    if v <= 4:
        return "Radius.XS"
    if v <= 6:
        return "Radius.SM"
    if v <= 9:
        return "Radius.MD"
    if v <= 12:
        return "Radius.LG"
    if v <= 16:
        return "Radius.XL"
    return None


def files():
    for entry in SCAN:
        if entry.is_file():
            yield entry
        elif entry.is_dir():
            yield from (p for p in entry.rglob("*.xaml") if "obj" not in p.parts and "bin" not in p.parts)


total = 0
for path in files():
    text = path.read_text(encoding="utf-8")
    hits = []

    def sub(m):
        tok = token_for(m.group("v"))
        if tok is None:
            return m.group(0)
        hits.append(tok)
        return f'CornerRadius="{{StaticResource {tok}}}"'

    updated = ATTR.sub(sub, text)
    if hits:
        path.write_text(updated, encoding="utf-8", newline="")
        total += len(hits)
print(f"Toplam: {total} CornerRadius tasindi.")
