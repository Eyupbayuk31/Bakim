"""
Sabit FontSize değerlerini tipografi ölçeğine taşır (MASTER_PLAN §3.2).

  FontSize="11"  ->  FontSize="{StaticResource Font.Caption}"
  <Setter Property="FontSize" Value="12"/>  ->  Value="{StaticResource Font.Caption}"

Simge boyutları (ui:SymbolIcon, ui:FontIcon, ProgressRing) METİN değildir; dokunulmaz.
Çok büyük vitrin boyutları (>= 48) da bırakılır. Betik tekrar çalıştırılabilir (idempotent).

Eşleme (okunabilirlik için küçük metin bir kademe büyür, en küçük gövde 12 olur):
  8-10      -> Font.Micro (11)
  10.5-12.5 -> Font.Caption (12)
  13-14     -> Font.Body (14)
  15        -> Font.BodyLarge (15)
  16        -> Font.Subtitle (16)
  17-18     -> Font.SectionTitle (18)
  19-22     -> Font.Title (20)
  23-28     -> Font.TitleLarge (28)
  29-47     -> Font.Display (40)

Çalıştırma:  python Tools/migrate-font-sizes.py [--check]
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
SCAN = [ROOT / "Views", ROOT / "Controls", ROOT / "MainWindow.xaml", ROOT / "App.xaml", ROOT / "LoginWindow.xaml"]
ICON_TAGS = ("ui:SymbolIcon", "ui:FontIcon", "ui:ProgressRing", "SymbolIcon", "ui:ImageIcon")

TAG = re.compile(r"<(?P<name>[A-Za-z_][\w:.]*)(?P<attrs>[^<>]*?)(?P<end>/?)>", re.S)
ATTR = re.compile(r'(\bFontSize=")(?P<v>[0-9]+(?:\.[0-9]+)?)(")')
SETTER = re.compile(r'(<Setter\s+Property="FontSize"\s+Value=")(?P<v>[0-9]+(?:\.[0-9]+)?)(")')


def token_for(value: float):
    if value >= 48:
        return None
    if value <= 10:
        return "Font.Micro"
    if value <= 12.5:
        return "Font.Caption"
    if value <= 14:
        return "Font.Body"
    if value <= 15:
        return "Font.BodyLarge"
    if value <= 16:
        return "Font.Subtitle"
    if value <= 18:
        return "Font.SectionTitle"
    if value <= 22:
        return "Font.Title"
    if value <= 28:
        return "Font.TitleLarge"
    return "Font.Display"


def files():
    for entry in SCAN:
        if entry.is_file():
            yield entry
        elif entry.is_dir():
            yield from (p for p in entry.rglob("*.xaml") if "obj" not in p.parts and "bin" not in p.parts)


def migrate(text: str):
    changes = 0

    def on_tag(m):
        nonlocal changes
        name = m.group("name")
        if name.startswith(ICON_TAGS):
            return m.group(0)

        def on_attr(a):
            nonlocal changes
            tok = token_for(float(a.group("v")))
            if tok is None:
                return a.group(0)
            changes += 1
            return f'{a.group(1)}{{StaticResource {tok}}}{a.group(3)}'

        return f"<{name}{ATTR.sub(on_attr, m.group('attrs'))}{m.group('end')}>"

    text = TAG.sub(on_tag, text)

    def on_setter(s):
        nonlocal changes
        tok = token_for(float(s.group("v")))
        if tok is None:
            return s.group(0)
        changes += 1
        return f'{s.group(1)}{{StaticResource {tok}}}{s.group(3)}'

    text = SETTER.sub(on_setter, text)
    return text, changes


def main():
    check = "--check" in sys.argv
    total = 0
    for path in files():
        original = path.read_text(encoding="utf-8")
        updated, n = migrate(original)
        if n:
            total += n
            rel = path.relative_to(ROOT).as_posix()
            print(f"{rel}: {n}")
            if not check:
                path.write_text(updated, encoding="utf-8", newline="")
    print(f"Toplam: {total} sabit FontSize {'bulundu' if check else 'tasindi'}.")
    return 1 if (check and total) else 0


if __name__ == "__main__":
    sys.exit(main())
