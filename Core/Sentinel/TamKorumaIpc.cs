using System;
using System.Text.Json;

namespace Bakım.Core.Sentinel
{
    public enum SentinelProtectionMode
    {
        TemelMod,
        /// <summary>Yönetici: yeni süreçler WMI olaylarıyla anında yakalanır; dosya atfı yok.</summary>
        Gelismis,
        /// <summary>Dosya ve kayıt olayları süreçle eşleştirilir (NÖB v3 Faz C; USN/ETW).</summary>
        TamKoruma
    }

    public sealed record SentinelProtectionStatus(
        SentinelProtectionMode Mode,
        string BadgeText,
        string Description,
        bool IsUsnActive,
        bool IsKernelTraceActive,
        string ActiveSensorsSummary);

    /// <summary>
    /// Nöbetçinin koruma düzeyi. Rozet yalnızca gerçekten çalışan sensörleri söyler (NÖB v3 A2):
    /// "Tam Koruma" dosya olaylarının süreçle eşleştirildiği moda ayrılmıştır.
    /// </summary>
    public static class SentinelProtectionPolicy
    {
        private const string Baseline = "Klasör izleyicisi · Kayıt defteri ve sistem ayarı karşılaştırması";
        private const string SharedCaveat = "Kurulumla aynı anda çalışan programların yazdıkları da rapora girebilir.";

        public static SentinelProtectionStatus Evaluate(bool isAdmin, bool isUsnAvailable, bool isTraceAvailable)
        {
            if (isAdmin && isUsnAvailable)
            {
                return new SentinelProtectionStatus(
                    Mode: SentinelProtectionMode.TamKoruma,
                    BadgeText: "Tam Koruma",
                    Description: "Dosya değişiklikleri NTFS değişiklik günlüğünden okunuyor, yeni süreçler anında yakalanıyor.",
                    IsUsnActive: true,
                    IsKernelTraceActive: isTraceAvailable,
                    ActiveSensorsSummary: "NTFS değişiklik günlüğü · " + (isTraceAvailable ? "Anlık süreç olayları · " : "") + Baseline);
            }

            if (isAdmin && isTraceAvailable)
            {
                return new SentinelProtectionStatus(
                    Mode: SentinelProtectionMode.Gelismis,
                    BadgeText: "Gelişmiş Mod",
                    Description: "Yönetici yetkisiyle kurulumun alt süreçleri anında yakalanıyor. Dosya değişiklikleri klasör izleyicisiyle kaydediliyor. " + SharedCaveat,
                    IsUsnActive: false,
                    IsKernelTraceActive: true,
                    ActiveSensorsSummary: "Anlık süreç olayları (WMI) · " + Baseline);
            }

            return new SentinelProtectionStatus(
                Mode: SentinelProtectionMode.TemelMod,
                BadgeText: "Temel Mod",
                Description: "Kurulumlar süreç taramasıyla algılanıyor, dosya değişiklikleri klasör izleyicisiyle kaydediliyor. " + SharedCaveat,
                IsUsnActive: false,
                IsKernelTraceActive: false,
                ActiveSensorsSummary: "Süreç taraması · " + Baseline);
        }
    }

    public enum SentinelIpcCommandKind
    {
        GetStatus = 1,
        StartSession = 2,
        EndSession = 3,
        PollUsn = 4
    }

    public sealed record SentinelIpcMessage(
        SentinelIpcCommandKind Command,
        string? Payload,
        DateTime TimestampUtc);

    public static class SentinelIpcSerializer
    {
        public static string Serialize(SentinelIpcMessage message) =>
            JsonSerializer.Serialize(message);

        public static SentinelIpcMessage? Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<SentinelIpcMessage>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
