using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakım.Models
{
    public enum ResidualType
    {
        Folder,
        File,
        RegistryKey,
        RegistryValue,
        Service,
        ScheduledTask,
        FirewallRule
    }

    public partial class ResidualItem : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private bool _isDeleted;

        [ObservableProperty]
        private bool _isDeleting;

        public string Path { get; set; } = string.Empty;
        public ResidualType Type { get; set; } = ResidualType.Folder;
        public long SizeInBytes { get; set; }
        public string Description { get; set; } = string.Empty;

        /// <summary>100 kesin, 90 yüksek, 60 orta, 30 düşük (bkz. LeftoverItem).</summary>
        public int ConfidenceScore { get; set; } = 60;

        /// <summary>Neden kalıntı sayıldığı.</summary>
        public string EvidenceText { get; set; } = string.Empty;

        /// <summary>Hizmet, görev ve güvenlik duvarı kuralı silmek yönetici onayı ister.</summary>
        public bool NeedsAdmin => Type is ResidualType.Service or ResidualType.ScheduledTask or ResidualType.FirewallRule;

        /// <summary>Kesin kanıtlı, bilinen köklerin dışındaki kurulum klasörü.</summary>
        public bool AllowOutsideKnownRoots { get; set; }

        /// <summary>Yüksek ve kesin güvenli öğeler "güvenli" sayılır ve varsayılan seçilir.</summary>
        public bool IsSafeToDelete => ConfidenceScore >= 90;

        public string FormattedSize => SizeInBytes > 0
            ? Bakım.Core.Text.ByteFormatter.Format(SizeInBytes)
            : Type switch
            {
                ResidualType.RegistryKey => "Kayıt Anahtarı",
                ResidualType.RegistryValue => "Kayıt Değeri",
                ResidualType.Service => "Hizmet",
                ResidualType.ScheduledTask => "Görev",
                ResidualType.FirewallRule => "Kural",
                _ => "0 B"
            };

        public string TypeName => Type switch
        {
            ResidualType.Folder => "Klasör",
            ResidualType.File => "Dosya",
            ResidualType.RegistryKey => "Kayıt Defteri",
            ResidualType.RegistryValue => "Kayıt Değeri",
            ResidualType.Service => "Hizmet",
            ResidualType.ScheduledTask => "Zamanlanmış Görev",
            ResidualType.FirewallRule => "Güvenlik Duvarı",
            _ => "Kalıntı"
        };

        public string TypeIconSymbol => Type switch
        {
            ResidualType.Folder => "Folder20",
            ResidualType.File => "Document20",
            ResidualType.RegistryKey => "Tag20",
            ResidualType.RegistryValue => "Tag20",
            ResidualType.Service => "Settings20",
            ResidualType.ScheduledTask => "CalendarClock20",
            ResidualType.FirewallRule => "Shield20",
            _ => "Apps20"
        };

        /// <summary>
        /// Mutlak ifade ("%100 Güvenli") kullanılmaz: kullanıcıya kanıtın gücü söylenir.
        /// </summary>
        public string RiskBadgeText => ConfidenceScore switch
        {
            >= 100 => "Kesin",
            >= 90 => "Yüksek güven",
            >= 60 => "İnceleyin",
            _ => "Düşük güven"
        };

        /// <summary>Kalıntının silinme güvenliğinin anlamsal tonu.</summary>
        public Intent RiskIntent => ConfidenceScore >= 90 ? Intent.Success : Intent.Caution;
        public string RiskBadgeBackground => IsSafeToDelete ? "#1510B981" : "#15F59E0B";
    }
}
