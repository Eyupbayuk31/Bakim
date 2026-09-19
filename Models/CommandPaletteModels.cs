namespace Bakım.Models
{
    public enum CommandActionKind
    {
        Navigate,
        ExecuteTweak,
        QuickAction
    }

    public class CommandPaletteItem
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "Genel";
        public string Description { get; set; } = string.Empty;
        public string IconName { get; set; } = "Apps24";
        public CommandActionKind ActionKind { get; set; } = CommandActionKind.Navigate;
        public string TargetParameter { get; set; } = string.Empty;
        public string KeyboardShortcut { get; set; } = string.Empty;
    }
}
