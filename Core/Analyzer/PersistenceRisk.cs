using System;
using System.Collections.Generic;

namespace Bakım.Core.Analyzer
{
    public enum SignatureState
    {
        Verified,
        Invalid,
        Unsigned
    }

    /// <summary>Kalıcılık girdisinin risk girdileri (Models.PersistenceItem'dan doldurulur).</summary>
    public sealed record PersistenceRiskInput(
        AutorunCategory Category,
        SignatureState Signature,
        bool SignatureApplicable,
        bool IsMicrosoft,
        bool HasFilePath,
        bool FileExists,
        bool IsEnabled,
        bool IsRecentlyAdded,
        int VirusTotalMalicious = -1,
        int VirusTotalSuspicious = 0,
        int VirusTotalTotal = -1,
        int CollectorRisk = 0,
        string? CollectorNotes = null,
        int? DeepScore = null,
        bool UserTrusted = false);

    /// <summary>Puan ve kullanıcıya gösterilecek gerekçeler ("neden bu puan?").</summary>
    public sealed record PersistenceRiskResult(int Score, IReadOnlyList<string> Reasons);

    /// <summary>
    /// Analizör satırının risk puanı (ANL Faz 2). Kural sırası:
    ///   1. Derin analiz yapıldıysa onun puanı esastır (daha fazla kanıta dayanır).
    ///   2. İmza (yalnızca anlamlıysa), konum riski, toplayıcı bulguları, yaş.
    ///   3. VirusTotal uzlaşısı taban koyar; imza ya da kullanıcı güveni bunu düşüremez.
    ///   4. Dosya yoksa: tehdit değil temizlik konusu. Devre dışı girdi: risk büyük ölçüde düşer.
    ///   5. Kullanıcının güvendiği dosya: VT kritik değilse puan tabana iner.
    /// </summary>
    public static class PersistenceRisk
    {
        public static PersistenceRiskResult Evaluate(PersistenceRiskInput i)
        {
            var reasons = new List<string>();
            int score;

            if (i.DeepScore is int deep)
            {
                score = deep;
                reasons.Add($"Derin dosya analizi: {deep}/100.");
            }
            else
            {
                score = i.CollectorRisk;
                if (!string.IsNullOrWhiteSpace(i.CollectorNotes) && i.CollectorRisk > 0) reasons.Add(i.CollectorNotes!);

                if (i.SignatureApplicable)
                {
                    if (i.Signature == SignatureState.Invalid)
                    {
                        score += 60;
                        reasons.Add("Dijital imza geçersiz ya da bozulmuş (dosya imzalandıktan sonra değiştirilmiş olabilir).");
                    }
                    else if (i.Signature == SignatureState.Unsigned)
                    {
                        score += i.IsMicrosoft ? 35 : 40;
                        reasons.Add("Dosyanın dijital imzası yok; yayıncı doğrulanamıyor.");
                    }
                }

                int location = i.Category switch
                {
                    AutorunCategory.WmiEventConsumer or AutorunCategory.WinlogonIfeo => 20,
                    AutorunCategory.ShellExtension => 10,
                    _ => 0
                };
                if (location > 0)
                {
                    score += location;
                    reasons.Add("Meşru yazılımların nadiren kullandığı bir kalıcılık konumu.");
                }

                if (i.IsRecentlyAdded)
                {
                    score += 10;
                    reasons.Add("Son 7 günde eklenmiş.");
                }
            }

            var vt = VirusTotalRules.Evaluate(Math.Max(0, i.VirusTotalMalicious), i.VirusTotalSuspicious, i.VirusTotalTotal > 0 ? i.VirusTotalTotal : 70);
            if (vt is { } floor && i.DeepScore == null)
            {
                score = Math.Max(score + floor.Impact, floor.MinimumScore);
                reasons.Add($"{floor.Title} ({Math.Max(0, i.VirusTotalMalicious)}/{(i.VirusTotalTotal > 0 ? i.VirusTotalTotal : 70)}).");
            }

            if (i.HasFilePath && !i.FileExists)
            {
                score = Math.Clamp(score - 40, 5, 20);
                reasons.Clear();
                reasons.Add("Hedef dosya yok: artık çalışmayan yetim kayıt (temizlik konusu, tehdit değil).");
            }

            if (!i.IsEnabled)
            {
                score = (int)(score * 0.4);
                reasons.Add("Girdi devre dışı: şu an çalışmıyor.");
            }

            if (i.UserTrusted)
            {
                if (vt is { Critical: true })
                {
                    reasons.Add("Güvenilir olarak işaretlediniz, ancak VirusTotal uzlaşısı bu kararı geçersiz kılıyor.");
                }
                else
                {
                    score = Math.Min(score, 5);
                    reasons.Clear();
                    reasons.Add("Bu dosyayı güvenilir olarak işaretlediniz.");
                }
            }

            score = Math.Clamp(score, 0, 100);
            if (reasons.Count == 0) reasons.Add(i.IsMicrosoft ? "Microsoft imzalı, doğrulanmış dosya." : "Belirgin bir risk işareti yok.");
            return new PersistenceRiskResult(score, reasons);
        }
    }
}
