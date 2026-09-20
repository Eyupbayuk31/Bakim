"""
ViewModel'lerde DI'ı derleyici zorunluluğu haline getirir.

Önce:
    public FooViewModel() : this(null) { }
    public FooViewModel(IBar? bar) { _bar = bar ?? new Bar(); }

Sonra:
    public FooViewModel(IBar bar) { _bar = bar; }

Neden: `?? new Bar()` yedeği DI'ı sessizce atlar. Konteyner bir kaydı unutsa
bile kod derlenir, çalışır ve YANLIŞ (tekil olmayan) örneği kullanır — tam da
v3.0'da tema servisinin çift örnekli olmasına yol açan hata buydu.
Yedek kaldırılınca eksik kayıt açılışta net bir istisnaya dönüşür.

Çalıştırma:  python Tools/enforce-di.py [--apply]
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).parent.parent
VMS = ROOT / "ViewModels"

# public XViewModel() : this(null, null) { }  ->  sil
PARAMLESS = re.compile(
    r'[ \t]*public\s+(\w+)\(\)\s*:\s*this\([^)]*\)\s*\r?\n'
    r'[ \t]*\{\s*\r?\n'
    r'[ \t]*\}\s*\r?\n\r?\n?',
    re.M)

# _field = param ?? new Concrete();   /   var x = param ?? new Concrete();
FALLBACK = re.compile(
    r'(?P<lhs>(?:var\s+\w+|_\w+|\w+))\s*=\s*(?P<param>\w+)\s*\?\?\s*new\s+\w+\([^)]*\)\s*;')

# Ctor imzasindaki nullable servis parametreleri:  IFoo? foo = null  /  IFoo? foo
NULLABLE_PARAM = re.compile(r'\b(I[A-Z]\w*)\?\s+(\w+)(\s*=\s*null)?')


def process(text):
    changes = []

    # 1) Parametresiz yapicilari kaldir
    def drop(m):
        changes.append(f"parametresiz ctor kaldirildi: {m.group(1)}()")
        return ""
    text = PARAMLESS.sub(drop, text)

    # 2) `?? new X()` yedeklerini kaldir
    def unfallback(m):
        changes.append(f"yedek kaldirildi: {m.group('lhs')} = {m.group('param')}")
        return f"{m.group('lhs')} = {m.group('param')};"
    text = FALLBACK.sub(unfallback, text)

    # 3) Yapici imzalarindaki nullable servis parametrelerini zorunlu yap
    #    Yalnizca ctor parametre listelerinde; govdedeki degiskenlere dokunma.
    def fix_ctor(m):
        head, params, tail = m.group(1), m.group(2), m.group(3)
        new_params, n = NULLABLE_PARAM.subn(r'\1 \2', params)
        if n:
            changes.append(f"{n} parametre zorunlu hale getirildi")
        return head + new_params + tail

    text = re.sub(
        r'(public\s+\w+ViewModel\s*\()([^)]*)(\))',
        fix_ctor, text, flags=re.S)

    return text, changes


def main():
    apply = "--apply" in sys.argv
    total = 0

    # Diyalog ViewModel'leri konteynerden cozulmez: calisma zamani verisiyle
    # (secilen uygulama, analiz sonucu) elle kurulurlar. Bu mesru bir fabrika
    # senaryosudur, DI ihlali degildir.
    SKIP = {"ResidualCleanupViewModel.cs", "ThreatAnalysisViewModel.cs"}

    for path in sorted(VMS.glob("*.cs")):
        if path.name in SKIP:
            continue
        original = path.read_text(encoding="utf-8")
        updated, changes = process(original)

        if not changes:
            continue

        print(f"  {path.name}")
        for c in changes:
            print(f"      {c}")
        total += len(changes)

        if apply:
            path.write_text(updated, encoding="utf-8")

    print(f"\nToplam degisiklik: {total}")
    print("(kuru calisma - yazmak icin --apply)" if not apply else "YAZILDI.")


if __name__ == "__main__":
    main()
