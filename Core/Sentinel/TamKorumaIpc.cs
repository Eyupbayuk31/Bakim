using System;
using System.Text.Json;

namespace Bakım.Core.Sentinel
{
    public enum SentinelProtectionMode
    {
        TemelMod,
        TamKoruma
    }

    public sealed record SentinelProtectionStatus(
        SentinelProtectionMode Mode,
        string BadgeText,
        string Description,
        bool IsUsnActive,
        bool IsKernelTraceActive,
        string ActiveSensorsSummary);

    public static class SentinelProtectionPolicy
    {
        public static SentinelProtectionStatus Evaluate(bool isAdmin, bool isUsnAvailable, bool isTraceAvailable)
        {
            if (isAdmin && (isUsnAvailable || isTraceAvailable))
            {
                var sensors = new System.Collections.Generic.List<string>();
                if (isUsnAvailable) sensors.Add("NTFS USN Değişiklik Günlüğü");
                if (isTraceAvailable) sensors.Add("Kernel Süreç Başlatma İzleyicisi");
                sensors.Add("FSW v2 Tamponu (64 KB)");
                sensors.Add("Kayıt Defteri Hotspot Sensörü");

                return new SentinelProtectionStatus(
                    Mode: SentinelProtectionMode.TamKoruma,
                    BadgeText: "Tam Koruma Modu",
                    Description: "Arka plan çekirdek izleyicisi ve NTFS USN günlüğü devrede. Kurulum hareketleri derinlemesine izleniyor.",
                    IsUsnActive: isUsnAvailable,
                    IsKernelTraceActive: isTraceAvailable,
                    ActiveSensorsSummary: string.Join(" · ", sensors));
            }

            return new SentinelProtectionStatus(
                Mode: SentinelProtectionMode.TemelMod,
                BadgeText: "Temel Mod",
                Description: "Standart kullanıcı modu devrede. Dosya sistemi (64 KB FSW) ve kayıt defteri sensörleri aktiftir.",
                IsUsnActive: false,
                IsKernelTraceActive: false,
                ActiveSensorsSummary: "FSW v2 Tamponu · Kayıt Defteri Hotspot Sensörü · Süreç Ağacı Takibi");
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
