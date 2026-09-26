using System;
using Bakım.Core.Sentinel;

namespace Bakım.Core.Risk
{
    /// <summary>
    /// Bakım'ın tek risk dili (ANL Faz 2). Analizör listesi, dosya analizi, Analizör geçmişi ve Kurulum
    /// Nöbetçisi aynı eşikleri ve etiketleri kullanır. Eskiden dört ayrı ölçek vardı (60/30, 61/26,
    /// 15/25/45/60, 16/40/70): aynı puan bir ekranda "Orta", diğerinde "Şüpheli" görünebiliyordu.
    ///
    ///   0–9 Temiz · 10–29 Bilgi · 30–49 Dikkat · 50–74 Şüpheli · 75–100 Tehlikeli
    /// </summary>
    public static class RiskScale
    {
        public const int Info = 10;
        public const int Caution = 30;
        public const int Suspicious = 50;
        public const int Dangerous = 75;

        /// <summary>Puandan karar. Bulgusu olan ama puanı düşük sonuç "Temiz" değil "Bilgi" sayılır.</summary>
        public static RiskVerdict Verdict(int score, bool hasFindings = false) => score switch
        {
            >= Dangerous => RiskVerdict.Dangerous,
            >= Suspicious => RiskVerdict.Suspicious,
            >= Caution => RiskVerdict.Caution,
            >= Info => RiskVerdict.Info,
            _ => hasFindings && score > 0 ? RiskVerdict.Info : RiskVerdict.Clean
        };

        public static string Label(RiskVerdict verdict) => verdict switch
        {
            RiskVerdict.Clean => "Temiz",
            RiskVerdict.Info => "Bilgi",
            RiskVerdict.Caution => "Dikkat",
            RiskVerdict.Suspicious => "Şüpheli",
            RiskVerdict.Dangerous => "Tehlikeli",
            _ => "Bilinmiyor"
        };

        public static string Label(int score, bool hasFindings = false) => Label(Verdict(score, hasFindings));

        /// <summary>Renkten bağımsız simge (renk körü kullanıcılar için).</summary>
        public static string Symbol(RiskVerdict verdict) => verdict switch
        {
            RiskVerdict.Dangerous or RiskVerdict.Suspicious => "ShieldDismiss24",
            RiskVerdict.Caution => "ShieldError24",
            _ => "ShieldCheckmark24"
        };

        /// <summary>"Yüksek riskli" sayaçları ve sıralama vurgusu: Şüpheli ve üstü.</summary>
        public static bool IsHigh(int score) => score >= Suspicious;
    }
}
