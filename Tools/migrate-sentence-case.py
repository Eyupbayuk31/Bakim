"""
Windows 11 metin kuralı (MASTER_PLAN §310, docs/FLUENT_SAYFA_PLANI.md G6):
başlıklar cümle düzeninde ("Temizlik hedefleri"), "&" yerine "ve".

Yalnızca TÜM sözcükleri büyük harfle başlayan kısa sabit metinlere dokunur (cümlelere değil).
Korunanlar: ürün ve özellik adları, kısaltmalar (CPU, DNS), rakam içeren sözcükler,
parantez içleri ve iki nokta / tireden sonraki ilk sözcük.

Çalıştırma:  python Tools/migrate-sentence-case.py [--apply]
"""
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).parent.parent
ATTRS = ("Text", "Content", "Title", "Header", "Description", "PlaceholderText", "ToolTip", "Label", "Caption", "Heading", "OnContent", "OffContent")

PROTECTED = [
    "Kayıt Defteri Düzenleyicisi", "Geri Dönüşüm Kutusu", "Olay Görüntüleyicisi", "Aygıt Yöneticisi", "Disk Yönetimi", "Windows Fotoğraf Görüntüleyicisi",
    "Görev Yöneticisi", "Görev Zamanlayıcı", "Görev Zamanlayıcısı", "Kurulum Nöbetçisi", "Etkinlik Merkezi",
    "Kontrol Paneli", "Denetim Masası", "Oyun Modu", "Hızlı Bakım", "Kayıt Defteri", "Disk Haritası",
    "Güvenlik Duvarı", "Dosya Gezgini", "Başlat Menüsü", "Görev Çubuğu", "Hızlı Ayarlar", "Ağ İzleyici",
    "Windows Ayarları", "Windows Araçları", "Windows Update", "Windows Defender", "Windows Gezgini",
    "Windows Hello", "Windows Terminal", "Microsoft Store", "Microsoft Edge", "Microsoft Teams",
    "Microsoft Defender", "Microsoft OneDrive", "Google Chrome", "Google Drive", "Mozilla Firefox",
    "Visual Studio Code", "Visual Studio", "Epic Games", "Sistem Geri Yükleme",
    "Windows", "Microsoft", "Bakım", "Steam", "Epic", "Edge", "Chrome", "Firefox", "Brave", "Vivaldi", "Opera",
    "Discord", "Telegram", "Spotify", "Teams", "OneDrive", "Dropbox", "Defender", "Mica", "Fluent", "VirusTotal",
    "Sysinternals", "Autoruns", "RAMMap", "NVIDIA", "Intel", "Realtek", "DirectX", "NuGet", "Office", "Outlook",
    "Xbox", "Cortana", "Copilot", "Bing", "Explorer", "TrustedInstaller", "PowerShell", "Ethernet", "Wi-Fi",
    "Hyper-V", "Bluetooth", "Adobe", "Java", "Python", "Winaero", "Türkiye", "Türkçe",
    "Olay Görüntüleyici", "Windows Sandbox", "Sandbox", "Analizör", "Nöbetçi",
    "Mbps", "Gbps", "KB/s", "MB/s", "GB/s",
]

ACRONYMS = {"RAM", "CPU", "GPU", "SSD", "HDD", "DNS", "PID", "UAC", "BSOD", "SFC", "DISM", "GPO", "OEM", "TCP",
            "UDP", "IP", "API", "CSV", "JSON", "USB", "WMI", "UWP", "BIOS", "UEFI", "PE", "VT", "MAC", "IPv4",
            "IPv6", "MB", "GB", "KB", "TB", "AMOLED", "UTC", "RVA", "NTFS", "PnP", "SHA-256", "MD5"}

TURKISH_HINT = re.compile(r"[çğıöşüÇĞİÖŞÜ0-9]|\b(Tara|Kategori|Kapat|Modu|Kapal)")

LOWER_MAP = str.maketrans({"I": "ı", "İ": "i"})

def tr_lower(word):
    return word.translate(LOWER_MAP).lower()

# Bağlaçlar küçük harfle yazılsa da başlığı Büyük Harfli saymayı engellemez ("Dosya Adı ve Konumu").
CONNECTORS = {"ve", "ile", "için", "veya", "ya", "da", "de", "bir"}

def is_title_case(text):
    words = [w for w in re.split(r"\s+", text.replace("&amp;", " ").strip())
             if w and w[0].isalpha() and w not in CONNECTORS]
    return len(words) >= 2 and all(w[0].isupper() for w in words)

def convert(text):
    if "\\" in text:   # dosya yolu
        return text
        return text
    letters = [c for c in text.replace("&amp;", "") if c.isalpha()]
    if letters and all(c.isupper() for c in letters):   # TAMAMI BÜYÜK etiket → cümle düzeni
        words = text.split(" ")
        out = []
        for i, w in enumerate(words):
            core = w.strip(".,:;!?&")
            if core in ACRONYMS or not core or w == "&amp;":
                out.append(w)
            elif w.startswith("("):
                out.append(w.lower())   # parantez içi çoğunlukla İngilizce terim (PAGEFILE → pagefile)
            else:
                low = tr_lower(w)
                out.append(low[:1].upper().replace("i", "İ") + low[1:] if i == 0 else low)
        return " ".join(out)
    placeholders = {}
    def keep(m):
        key = f"\x00{len(placeholders)}\x00"
        placeholders[key] = m.group(0)
        return key
    work = text
    for phrase in sorted(PROTECTED, key=len, reverse=True):
        # Türkçe ekler de korunur: "Analizörde", "Oyun Modunu", "DNS'i"
        work = re.sub(rf"(?<![\w]){re.escape(phrase)}(?:'[a-zçğıöşü]+|[a-zçğıöşü]{{0,4}})(?![\w])", keep, work)
    # İngilizce parantez içi (Uninstall, Explorer) korunur; Türkçe olan ("(7 Gün)") küçültülür.
    def paren(m):
        inner = m.group(1)
        if TURKISH_HINT.search(inner):
            words = inner.split(" ")
            return "(" + " ".join(w if (w.strip(".,/") in ACRONYMS or "\x00" in w) else tr_lower(w[:1]) + w[1:] for w in words) + ")"
        return keep(m)
    work = re.sub(r"\(([^)]*)\)", paren, work)
    out, first, after_break = [], True, False
    for token in re.split(r"(\s+)", work):
        if not token or token.isspace() or "\x00" in token:
            out.append(token)
            if token and not token.isspace(): first = False
            continue
        core = token.strip(".,:;!?'\"—-–•/")
        keep_case = (first or after_break or core.isupper() or core in ACRONYMS
                     or any(c.isdigit() for c in core) or not core[:1].isalpha()
                     or (len(core) > 1 and any(c.isupper() for c in core[1:])))
        out.append(token if keep_case else tr_lower(token[:1]) + token[1:])
        first = False
        after_break = token.endswith((":", ".", "—", "–", "!", "?")) or token in ("-", "—", "–", "•")
    result = "".join(out)
    for k, v in reversed(list(placeholders.items())):   # iç içe yer tutucular için ters sırada
        result = result.replace(k, v)
    return result

def process(value):
    new = value
    if is_title_case(new):
        new = convert(new)
    new = re.sub(r"\s&amp;\s", " ve ", new)
    return new

def main(apply):
    changed = 0
    samples = []
    attr_re = re.compile(r'(?<![\w.:])(' + "|".join(ATTRS) + r')="([^"{][^"]*)"')
    for path in sorted(list((ROOT / "Views").rglob("*.xaml")) + [ROOT / "MainWindow.xaml", ROOT / "LoginWindow.xaml"]):
        text = path.read_text(encoding="utf-8")
        def rep(m):
            nonlocal changed
            new = process(m.group(2))
            if new != m.group(2):
                changed += 1
                if len(samples) < 400:
                    samples.append(f"{path.name}: {m.group(2)!r} -> {new!r}")
            return f'{m.group(1)}="{new}"'
        new_text = attr_re.sub(rep, text)
        if apply and new_text != text:
            path.write_text(new_text, encoding="utf-8")
    print("\n".join(samples))
    print(f"\n{changed} metin {'değiştirildi' if apply else 'değişecek (kuru çalışma)'}.")

if __name__ == "__main__":
    main("--apply" in sys.argv)
