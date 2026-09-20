using System.Threading.Tasks;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Ekranda görünürken kaynak tüketen (zamanlayıcı, WMI sorgusu, dinleyici)
    /// modüllerin uyması gereken yaşam döngüsü sözleşmesi.
    ///
    /// v3.1'e kadar dört modül (Pano, Ağ, Optimize Edici, Sistem Bilgisi) kendi
    /// DispatcherTimer'ını yapıcı metotta başlatıyor ve HİÇ durdurmuyordu.
    /// Uygulama açılışında hepsi birden ayağa kalkıp arka planda bile saniyede
    /// bir WMI sorgusu atıyordu. Bu arayüz o davranışı sonlandırır:
    /// yalnızca görünen modül veri toplar.
    /// </summary>
    public interface IModuleViewModel
    {
        /// <summary>Modül görünür oldu: zamanlayıcıları başlat, ilk veriyi çek.</summary>
        Task OnActivatedAsync();

        /// <summary>Modülden çıkıldı: zamanlayıcıları durdur, kaynakları bırak.</summary>
        Task OnDeactivatedAsync();
    }
}
