using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace Bakım.Models
{
    public partial class ToastNotificationItem : ObservableObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Success;
        public string IconName { get; set; } = "CheckmarkCircle24";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [ObservableProperty]
        private bool _isDismissed;

        /// <summary>İsteğe bağlı eylem düğmesi (ör. "Geri al" — UndoToast, §3.5).</summary>
        public string? ActionText { get; set; }

        /// <summary>Eylem: Etkinlik Merkezi kaydının kimliği (Geri al) ya da başka bir anahtar.</summary>
        public string? ActionArgument { get; set; }

        public bool HasAction => !string.IsNullOrEmpty(ActionText);

        [ObservableProperty]
        private bool _isActionRunning;

        public string SeverityBrushKey => Severity switch
        {
            InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
            InfoBarSeverity.Warning => "SystemFillColorCautionBrush",
            InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "AccentTextFillColorPrimaryBrush"
        };
    }
}
