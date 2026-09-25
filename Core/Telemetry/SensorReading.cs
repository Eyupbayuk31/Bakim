namespace Bakım.Core.Telemetry
{
    public enum SensorQuality
    {
        /// <summary>Donanımdan okunan gerçek değer.</summary>
        Measured,
        /// <summary>Hesaplanmış/tahmini değer; arayüzde "≈" ile gösterilir.</summary>
        Estimated,
        /// <summary>Okunamadı; arayüzde "—" gösterilir.</summary>
        Unavailable
    }

    /// <summary>
    /// Bir sensör ölçümü. Değer ile birlikte kalitesini taşır; arayüz
    /// tahmini değeri ölçüm gibi, olmayan değeri sayı gibi göstermez.
    /// </summary>
    public readonly record struct SensorReading(double? Value, SensorQuality Quality, string Unit, string Source)
    {
        public static SensorReading Unavailable(string unit, string reason) => new(null, SensorQuality.Unavailable, unit, reason);
        public static SensorReading Measured(double value, string unit, string source) => new(value, SensorQuality.Measured, unit, source);

        public bool HasValue => Value.HasValue && Quality != SensorQuality.Unavailable;

        /// <summary>"62 °C", "≈62 °C" ya da "—".</summary>
        public string Display => !HasValue
            ? "—"
            : (Quality == SensorQuality.Estimated ? "≈" : string.Empty) + $"{Value!.Value:0} {Unit}";
    }
}
