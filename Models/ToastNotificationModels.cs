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

        public string SeverityBrushKey => Severity switch
        {
            InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
            InfoBarSeverity.Warning => "SystemFillColorCautionBrush",
            InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "AccentTextFillColorPrimaryBrush"
        };
    }
}
