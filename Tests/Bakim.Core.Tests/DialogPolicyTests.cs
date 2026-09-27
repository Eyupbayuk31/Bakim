using Bakım.Core.Ui;
using Xunit;

namespace Bakim.Core.Tests;

/// <summary>Analizör v3 Faz 5: onay penceresinde birincil düğmenin kırmızı olup olmayacağı.</summary>
public class DialogPolicyTests
{
    [Theory]
    [InlineData("Kuralı Kaldır", "'X' güvenlik duvarı engelleme kuralını kaldırmak istiyor musunuz?")]
    [InlineData("Geri Dönüşüm Kutusu", "Geri Dönüşüm Kutusu boşaltılacak. Bu işlem geri alınamaz.")]
    [InlineData("Süreç", "Chrome (PID: 4) sürecini SONLANDIRMAK istiyor musunuz?")]
    [InlineData("Dosyalar", "3 dosya kalıcı olarak silinecek.")]
    [InlineData("Ayarları Sıfırla", "Tüm uygulama ayarlarını fabrika varsayılanlarına sıfırlamak istediğinize emin misiniz?")]
    [InlineData("Zorla Kaldır", "Uninstaller komutu atlanacak.")]
    public void Destructive(string title, string message) => Assert.True(DialogPolicy.IsDestructive(title, message));

    [Theory]
    [InlineData("Güncelleme", "5 program sırayla güncellenecek. Devam edilsin mi?")]
    [InlineData("Tarifeli Ağ Uyarısı", "Hız testi yaklaşık 100 MB veri kullanır.")]
    [InlineData("Geri al", "Bu işlem geri alınsın mı?")]
    [InlineData("Simge", "Masaüstü simgesi eklensin mi?")]
    public void NotDestructive(string title, string message) => Assert.False(DialogPolicy.IsDestructive(title, message));
}
