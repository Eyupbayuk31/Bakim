"""
Sessiz bağlama hatası taraması.

WPF'te var olmayan bir özelliğe bağlama İSTİSNA FIRLATMAZ: değer sessizce varsayılana düşer
(ör. ui:Button.Appearance → Primary, bu yüzden bozuk bağlı düğmeler mavi görünür).
Bu betik, sayfa görünümlerinde DataContext'e (görünüm modeline) giden basit {Binding Ad}
yollarını görünüm modelinin kaynağında arar. Şablon içleri (DataTemplate) satır modeline
bağlandığı için atlanır.

Çalıştırma:  python Tools/verify-bindings.py
Çıkış kodu:  0 = temiz, 1 = bulunamayan bağlama var
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent

def class_sources(name):
    out = []
    for p in list((ROOT / "ViewModels").rglob("*.cs")):
        t = p.read_text(encoding="utf-8", errors="ignore")
        if re.search(rf"\bclass\s+{name}\b", t):
            out.append(t)
    return out

def has_member(sources, member):
    field = "_" + member[0].lower() + member[1:]
    pat = re.compile(rf"(\b{member}\b\s*(\{{|=>|;|\())|(\b{field}\b)|(\bIRelayCommand\b.*\b{member}\b)")
    cmd = member.endswith("Command") and re.compile(rf"\b(Task|void)\s+{member[:-7]}(Async)?\s*\(")
    for t in sources:
        if pat.search(t) or (cmd and cmd.search(t)):
            return True
    return False

def base_members(sources):
    # temel sınıf / arayüz üyeleri için kaba kalıtım takibi
    bases = set()
    for t in sources:
        for m in re.finditer(r"class\s+\w+\s*:\s*([\w\s,<>.]+)", t):
            for b in m.group(1).split(","):
                b = b.strip().split("<")[0].split(".")[-1]
                if b and not b.startswith("I") and b != "ObservableObject":
                    bases.add(b)
    return bases

TEMPLATE_TAGS = {"DataTemplate", "HierarchicalDataTemplate", "ControlTemplate", "ItemsPanelTemplate", "Style"}

def page_paths(xaml):
    """DataContext'i değişmeyen (şablon dışı) öğelerdeki basit bağlama yolları."""
    from xml.etree import ElementTree as ET
    text = re.sub(r'xmlns(:\w+)?="[^"]*"', "", xaml)          # ad alanlarını at
    text = re.sub(r"<(/?)([\w]+):", r"<\1\2_", text)          # ui:Button → ui_Button
    text = re.sub(r"\s([\w]+):([\w.]+)=", r" \1_\2=", text)   # d:DataContext → d_DataContext
    root = ET.fromstring(text)
    paths = set()

    def walk(el, is_root):
        tag = el.tag.split("_")[-1].split(".")[0]
        if tag in TEMPLATE_TAGS:
            return
        if not is_root and "DataContext" in el.attrib:
            return  # alt ağaç başka bir nesneye bağlı
        for k, v in el.attrib.items():
            for bm in re.finditer(r"\{Binding\s+(?:Path=)?([A-Za-z_][\w.]*)?([^}]*)\}", v):
                if not bm.group(1) or bm.group(1).startswith(("Source", "ElementName", "RelativeSource", "Converter")):
                    continue
                rest = bm.group(2)
                if "ElementName" in rest or "RelativeSource" in rest or "Source=" in rest:
                    continue
                paths.add(bm.group(1).split(".")[0])
        for c in el:
            walk(c, False)

    walk(root, True)
    return paths

missing_total = 0
for view in sorted((ROOT / "Views" / "Modules").glob("*.xaml")):
    xaml = view.read_text(encoding="utf-8", errors="ignore")
    m = re.search(r'd:DesignInstance\s+Type=vm:(\w+)', xaml)
    if not m:
        continue
    vm = m.group(1)
    sources = class_sources(vm)
    for b in base_members(sources):
        sources += class_sources(b)
    if not sources:
        continue
    paths = page_paths(xaml)
    missing = sorted(p for p in paths if not has_member(sources, p))
    if missing:
        missing_total += len(missing)
        print(f"{view.name} ({vm}): {', '.join(missing)}")

if missing_total:
    print(f"\nHATA: {missing_total} bağlama görünüm modelinde bulunamadı.")
    sys.exit(1)
print("[OK] Sayfa bağlamalarının hepsi görünüm modelinde var.")
