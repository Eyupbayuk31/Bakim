using System;
using System.Collections.Generic;

namespace Bakım.Core.Network
{
    public sealed record TracerouteHop(
        int HopNumber,
        string Address,
        string Hostname,
        long RoundtripTimeMs,
        bool Success,
        string StatusText)
    {
        public string DisplayText => Success
            ? $"{HopNumber,2}.  {Address,-16}  {(RoundtripTimeMs < 0 ? "<1" : RoundtripTimeMs.ToString()),4} ms  {Hostname}"
            : $"{HopNumber,2}.  *                   —   (Zaman aşımı / yanıt yok)";
    }

    public sealed record DnsBenchmarkServer(
        string Provider,
        string PrimaryIp,
        string SecondaryIp,
        string Description)
    {
        public static IReadOnlyList<DnsBenchmarkServer> DefaultServers { get; } = new[]
        {
            new DnsBenchmarkServer("Cloudflare", "1.1.1.1", "1.0.0.1", "Hızlı ve gizlilik odaklı DNS"),
            new DnsBenchmarkServer("Google", "8.8.8.8", "8.8.4.4", "Yüksek kararlılık ve küresel anycast ağı"),
            new DnsBenchmarkServer("Quad9", "9.9.9.9", "149.112.112.112", "Tehdit ve zararlı yazılım engellemeli"),
            new DnsBenchmarkServer("OpenDNS", "208.67.222.222", "208.67.220.220", "Cisco altyapısıyla güvenilir"),
            new DnsBenchmarkServer("AdGuard", "94.140.14.14", "94.140.15.15", "Reklam ve izleyici filtrelemeli"),
            new DnsBenchmarkServer("Comodo Secure", "8.26.56.26", "8.20.247.20", "Zararlı web sitesi kalkanı"),
        };
    }

    public sealed record DnsBenchmarkResult(
        string Provider,
        string PrimaryIp,
        long LatencyMs,
        bool Success,
        string StatusMessage,
        bool IsFastest = false)
    {
        public string DisplayLatency => Success ? $"{LatencyMs} ms" : "Erişilemedi";
    }

    public static class MeteredNetworkPolicy
    {
        public static string DescribeCost(bool isMetered, bool isRoaming)
        {
            if (isRoaming) return "Dolaşım Modunda (Yüksek Veri Maliyeti)";
            if (isMetered) return "Ölçülü / Tarifeli Bağlantı (Kota Tüketimi)";
            return "Sınırsız / Standart Bağlantı";
        }

        public static string SpeedTestWarningText =>
            "Tarifeli / kotalı bir ağdasınız. Hız testi ~50-100 MB veri transferi gerçekleştirecektir. Devam etmek istiyor musunuz?";
    }
}
