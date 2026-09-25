using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bakım.Helpers
{
    /// <summary>
    /// Bir işlemin (ör. bir ince ayarın) yaptığı yazımların başarısızlıklarını toplar:
    /// kayıt defteri yazımları (<see cref="VerifiedRegistry"/>) ve yardımcı komutlar
    /// (<see cref="ProcessRunner"/>).
    ///
    /// Eski ince ayar servislerinin yardımcı metotları her hatayı <c>catch { }</c>
    /// ile yutuyor, ardından <c>return true</c> diyordu: yönetici olmadan HKLM'ye
    /// yazılamasa bile arayüz "başarıyla uygulandı" gösteriyordu (D-3).
    ///
    /// Kullanım: <c>using var writes = WriteScope.Begin();</c> → yazımlar
    /// <see cref="VerifiedRegistry"/> / <see cref="ProcessRunner"/> üzerinden yapılır → <see cref="Succeeded"/>
    /// kontrol edilir. Kapsamlar iç içe açılabilir; bir hata tüm üst kapsamlara da işlenir.
    /// </summary>
    public sealed class WriteScope : IDisposable
    {
        private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

        private readonly WriteScope? _parent;
        private readonly List<string> _failures = new();
        private bool _disposed;

        private WriteScope(WriteScope? parent) => _parent = parent;

        public static WriteScope Begin()
        {
            var scope = new WriteScope(CurrentScope.Value);
            CurrentScope.Value = scope;
            return scope;
        }

        public IReadOnlyList<string> Failures
        {
            get { lock (_failures) return _failures.ToList(); }
        }

        public bool Succeeded
        {
            get { lock (_failures) return _failures.Count == 0; }
        }

        /// <summary>Kullanıcıya gösterilecek kısa özet (ilk iki hata + kalan sayısı).</summary>
        public string Describe()
        {
            var list = Failures;
            if (list.Count == 0) return string.Empty;
            string head = string.Join("; ", list.Take(2));
            return list.Count > 2 ? $"{head} (+{list.Count - 2} hata daha)" : head;
        }

        /// <summary>Etkin kapsama (ve üst kapsamlara) bir başarısızlık ekler. Kapsam yoksa etkisizdir.</summary>
        public static void Report(string failure)
        {
            for (var scope = CurrentScope.Value; scope != null; scope = scope._parent)
            {
                lock (scope._failures) scope._failures.Add(failure);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ReferenceEquals(CurrentScope.Value, this))
                CurrentScope.Value = _parent;
        }
    }
}
