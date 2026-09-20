"""
Erişilebilirlik migrasyonu: etkileşimli öğelere AutomationProperties.Name ekler.

Ekran okuyucu bir düğmenin ne yaptığını yalnızca erişilebilir adından bilir.
Simge-only düğmeler ve yanındaki TextBlock'a dayanan anahtarlar, ekran
okuyucuda "düğme" / "anahtar" diye okunur — yani kullanılamaz.

Bu araç adı GÜVENLE türetilebilen durumları otomatik doldurur:
  * ui:TextBox  -> PlaceholderText
  * ui:Button   -> ToolTip
Türetilemeyenleri rapor eder; onlar elle yazılır.

Çalıştırma:  python add-automation-names.py [--apply]
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent  # depo koku
SKIP = {"bin", "obj", ".git", ".vs", "ui-ux-pro-max-skill", ".agents"}

TAGS = ("ui:Button", "Button", "ui:ToggleSwitch", "CheckBox", "ui:TextBox", "TextBox", "ComboBox")
ELEM = re.compile(r'<(' + "|".join(re.escape(t) for t in TAGS) + r')\b(.*?)(/>|>)', re.S)

ATTR = {
    "tooltip": re.compile(r'\bToolTip="([^"]+)"'),
    "placeholder": re.compile(r'\bPlaceholderText="([^"]+)"'),
    "content": re.compile(r'\bContent="([^"]+)"'),
}


def derive(tag, attrs):
    """Adı güvenle türetilebiliyorsa döndür, yoksa None."""
    tip = ATTR["tooltip"].search(attrs)
    ph = ATTR["placeholder"].search(attrs)

    if tag in ("ui:TextBox", "TextBox") and ph:
        return ph.group(1)
    if tip:
        # ToolTip bazen "&#10;" ile çok satırlı; ilk satırı al
        return tip.group(1).split("&#10;")[0].split("&#x0a;")[0].strip()
    return None


def main():
    apply = "--apply" in sys.argv
    filled = 0
    manual = []

    for path in ROOT.rglob("*.xaml"):
        if any(p in SKIP for p in path.parts):
            continue

        text = path.read_text(encoding="utf-8")
        out = []
        last = 0
        changed = False

        for m in ELEM.finditer(text):
            tag, attrs, close = m.group(1), m.group(2), m.group(3)

            if "AutomationProperties.Name" in attrs:
                continue
            # Metin içeriği olan düğme zaten erişilebilir ad taşır
            if tag in ("ui:Button", "Button") and (ATTR["content"].search(attrs) or close == ">"):
                continue

            name = derive(tag, attrs)
            rel = path.relative_to(ROOT).as_posix()

            if not name:
                snippet = " ".join(attrs.split())[:110]
                manual.append(f"{rel}:{text[:m.start()].count(chr(10)) + 1}  <{tag} {snippet}")
                continue

            escaped = name.replace('"', "&quot;")
            insert = f' AutomationProperties.Name="{escaped}"'
            out.append(text[last:m.end(2)])
            out.append(insert)
            last = m.end(2)
            filled += 1
            changed = True

        if changed:
            out.append(text[last:])
            new_text = "".join(out)
            print(f"  {path.relative_to(ROOT).as_posix()}")
            if apply:
                path.write_text(new_text, encoding="utf-8")

    print(f"\nOtomatik dolduruldu: {filled}")
    print(f"Elle yazilmasi gereken: {len(manual)}\n")

    if manual:
        print("ELLE:")
        for line in manual:
            print(f"  {line}")

    print("\n(kuru calisma - yazmak icin --apply)" if not apply else "\nYAZILDI.")


if __name__ == "__main__":
    main()
