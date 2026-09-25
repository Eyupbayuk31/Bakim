namespace Bakım.Models
{
    /// <summary>Risk düzeyi — Analizör, Nöbetçi ve Kaldırıcı için tek kaynak (§3.5 RiskBadge).</summary>
    public enum RiskLevel { Clean, Low, Medium, High, Critical }

    /// <summary>StatusPill durumları (§3.5).</summary>
    public enum PillState { Enabled, Disabled, Running, Stopped, AdminRequired, RestartRequired, Unknown }

    /// <summary>Bir ölçümün niteliği (D-1'in arayüz tarafı): ölçüldü / tahmin / yok.</summary>
    public enum MeasureQuality { Measured, Estimated, Unavailable }

    /// <summary>KeyValueGrid satırı.</summary>
    public sealed record KeyValueRow(string Key, string Value);
}
