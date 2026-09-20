namespace Bakım.Controls
{
    /// <summary>
    /// Bir öğenin anlamsal tonu. Renk seçimini çağrı yerinden alıp tasarım sistemine taşır:
    /// artık "yeşil" değil "başarı" denir, hangi yeşil olduğuna tema karar verir.
    /// </summary>
    public enum Intent
    {
        /// <summary>Nötr — varsayılan kart yüzeyi ve kenarlığı.</summary>
        Neutral,

        /// <summary>Bilgi / vurgu — canlı veri, aktif durum.</summary>
        Accent,

        /// <summary>Başarı — tamamlanmış, güvenli, sağlıklı.</summary>
        Success,

        /// <summary>Uyarı — dikkat gerektiren ama engelleyici olmayan.</summary>
        Caution,

        /// <summary>Kritik — tehdit, hata, geri alınamaz işlem.</summary>
        Critical
    }
}
