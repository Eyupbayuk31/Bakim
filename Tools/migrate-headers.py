"""
Modül başlıklarını tasarım sistemi bileşenine taşır.

Her modül aynı 20-25 satırlık bloğu elle yeniden yazıyordu:
    <Grid Grid.Row="0" Margin="0,0,0,20">
        <Grid.ColumnDefinitions>*, Auto</Grid.ColumnDefinitions>
        <StackPanel Grid.Column="0">
            <TextBlock FontSize="24" FontWeight="Bold" .../>   <- baslik
            <TextBlock FontSize="13" .../>                      <- aciklama
        </StackPanel>
        <... Grid.Column="1">eylemler</...>
    </Grid>

Bunu <c:SectionHeader .../> ile değiştirir.

GÜVENLİK: Dönüşüm yalnızca blok TAM OLARAK bu şekle uyduğunda yapılır.
Şüpheli her blok atlanır ve raporlanır — kör dönüşüm yoktur.

Çalıştırma:  python Tools/migrate-headers.py [--apply]
"""
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent
MODULES = ROOT / "Views" / "Modules"

TAG_OPEN = re.compile(r'<Grid\b')
TITLE_TB = re.compile(
    r'<TextBlock\s+Text="(?P<title>[^"]*)"\s+'
    r'FontSize="24"\s+FontWeight="Bold"\s+'
    r'Foreground="\{DynamicResource TextFillColorPrimaryBrush\}"\s*/>',
    re.S)
DESC_TB = re.compile(
    r'<TextBlock\s+Text="(?P<desc>[^"]*)"\s+'
    r'FontSize="13"\s+'
    r'Foreground="\{DynamicResource TextFillColorSecondaryBrush\}"\s+'
    r'Margin="0,4,0,0"\s*/>',
    re.S)
COLDEFS = re.compile(
    r'<Grid\.ColumnDefinitions>\s*'
    r'<ColumnDefinition\s+Width="\*"\s*/>\s*'
    r'<ColumnDefinition\s+Width="Auto"\s*/>\s*'
    r'</Grid\.ColumnDefinitions>', re.S)


def find_block(text, start):
    """start konumundaki <Grid ...> etiketinin dengeli kapanışını bulur."""
    depth = 0
    i = start
    while i < len(text):
        if text.startswith("<Grid", i) and (i == start or text[i + 5] in " \t\r\n>"):
            # self-closing mi?
            end_tag = text.find(">", i)
            if end_tag == -1:
                return None
            if text[end_tag - 1] == "/":
                i = end_tag + 1
                continue
            depth += 1
            i = end_tag + 1
            continue
        if text.startswith("</Grid>", i):
            depth -= 1
            if depth == 0:
                return i + len("</Grid>")
            i += 7
            continue
        i += 1
    return None


def transform(text, filename):
    """İlk uyan başlık bloğunu dönüştürür. (yeni_metin, rapor) döner."""
    for m in TAG_OPEN.finditer(text):
        open_end = text.find(">", m.start())
        if open_end == -1:
            continue
        open_tag = text[m.start():open_end + 1]

        # Yalnızca modülün kök satırındaki başlık ızgarası ilgilendiriyor
        if 'Grid.Row="0"' not in open_tag:
            continue

        end = find_block(text, m.start())
        if end is None:
            continue
        block = text[m.start():end]

        # --- Katı şekil denetimi ---
        if not COLDEFS.search(block):
            return None, f"{filename}: sutun tanimi beklenen sekilde degil"

        tm = TITLE_TB.search(block)
        dm = DESC_TB.search(block)
        if not tm or not dm:
            return None, f"{filename}: baslik/aciklama TextBlock deseni eslesmedi"

        # Başlık StackPanel'i tam olarak bu iki TextBlock'u mu içeriyor?
        sp_start = block.find('<StackPanel Grid.Column="0">')
        if sp_start == -1:
            return None, f"{filename}: Grid.Column=0 StackPanel bulunamadi"
        sp_end = block.find("</StackPanel>", sp_start)
        inner = block[sp_start + len('<StackPanel Grid.Column="0">'):sp_end]
        if inner.count("<TextBlock") != 2 or "<StackPanel" in inner:
            return None, f"{filename}: baslik blogu ek oge iceriyor (rozet?), elle tasinmali"

        # --- Eylem bölümü: StackPanel kapanışından Grid kapanışına kadar ---
        actions = block[sp_end + len("</StackPanel>"):block.rfind("</Grid>")].strip()
        actions = re.sub(r'\s*Grid\.Column="1"', "", actions, count=1)

        margin_m = re.search(r'Margin="([^"]+)"', open_tag)
        margin = margin_m.group(1) if margin_m else "0,0,0,18"

        title = tm.group("title")
        desc = dm.group("desc")

        indent = " " * 8
        lines = [
            f'{indent}<!-- Sayfa basligi — tasarim sistemi bileseni -->',
            f'{indent}<c:SectionHeader Grid.Row="0"',
            f'{indent}                 IsPageTitle="True"',
            f'{indent}                 HeaderMargin="{margin}"',
            f'{indent}                 Title="{title}"',
            f'{indent}                 Description="{desc}">',
        ]
        if actions:
            reindented = "\n".join(
                (indent + "        " + ln.strip()) if ln.strip() else ""
                for ln in actions.splitlines())
            lines.append(f'{indent}    <c:SectionHeader.Actions>')
            lines.append(reindented)
            lines.append(f'{indent}    </c:SectionHeader.Actions>')
        lines.append(f'{indent}</c:SectionHeader>')

        new_block = "\n".join(lines)
        new_text = text[:m.start()] + new_block + text[end:]

        # xmlns:c ekle
        if 'xmlns:c="clr-namespace:Bakım.Controls"' not in new_text:
            new_text = new_text.replace(
                'xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"',
                'xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"\n             xmlns:c="clr-namespace:Bakım.Controls"',
                1)

        saved = block.count("\n") - new_block.count("\n")
        return new_text, f"{filename}: '{title}' -> SectionHeader ({saved} satir azaldi)"

    return None, f"{filename}: Grid.Row=0 basligi bulunamadi"


def main():
    apply = "--apply" in sys.argv
    done = skipped = 0

    for path in sorted(MODULES.glob("*.xaml")):
        text = path.read_text(encoding="utf-8")

        if "<c:SectionHeader" in text:
            print(f"  ZATEN  {path.name}")
            continue

        new_text, report = transform(text, path.name)
        if new_text is None:
            print(f"  ATLA   {report}")
            skipped += 1
            continue

        print(f"  TASI   {report}")
        done += 1
        if apply:
            path.write_text(new_text, encoding="utf-8")

    print(f"\nTasinan: {done}, atlanan: {skipped}")
    print("(kuru calisma - yazmak icin --apply)" if not apply else "YAZILDI.")


if __name__ == "__main__":
    main()
