using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakım.Core.GameMode
{
    public enum GameModeStepState
    {
        /// <summary>Oyun Modu açılınca uygulanacak.</summary>
        WillApply,
        /// <summary>Profilde kapalı; uygulanmayacak.</summary>
        Skipped,
        /// <summary>Bu oturumda uygulandı.</summary>
        Applied,
        /// <summary>Bu oturumda uygulanamadı.</summary>
        Failed
    }

    /// <param name="Icon">Fluent sembol adı (SymbolRegular).</param>
    public sealed record GameModeStep(string Id, string Icon, string Title, string Detail, GameModeStepState State);

    /// <summary>Kullanıcının Oyun Modu profili (ayarlardan).</summary>
    public sealed record GameModeProfile(
        string PowerPlan,
        bool TrimMemory,
        IReadOnlyList<string> SuspendApps,
        bool AutoStart,
        IReadOnlyList<string> AutoStartGames);

    /// <summary>Etkin bir oturumda gerçekten ne yapıldığı.</summary>
    public sealed record GameModeSessionInfo(
        DateTime StartedAtUtc,
        string? TriggerGame,
        string? AppliedPowerPlan,
        bool PowerPlanFailed,
        long FreedBytes,
        IReadOnlyList<string> SuspendedApps,
        int SuspendedProcessCount)
    {
        public bool IsAutomatic => TriggerGame != null;
    }

    /// <summary>
    /// "Açınca ne olacak" listesi profilden üretilir; statik metin kullanılmaz, böylece ekran
    /// hiçbir zaman profille çelişmez (ör. bellek kırpma kapalıyken "kırpılır" yazmaz).
    /// Oturum açıkken aynı liste gerçekte ne olduğunu gösterir.
    /// </summary>
    public static class GameModePlan
    {
        public const string Keep = "Keep";
        public const string Ultimate = "Ultimate";
        public const string HighPerformance = "HighPerformance";

        public static string PowerPlanName(string? key) => key switch
        {
            Ultimate => "Nihai Performans",
            Keep => "Değiştirilmez",
            _ => "Yüksek Performans"
        };

        public static IReadOnlyList<GameModeStep> Build(GameModeProfile profile, GameModeSessionInfo? session = null)
        {
            bool active = session != null;
            var steps = new List<GameModeStep>();

            // 1) Güç planı
            bool keepPlan = string.Equals(profile.PowerPlan, Keep, StringComparison.OrdinalIgnoreCase);
            if (active)
            {
                steps.Add(session!.PowerPlanFailed
                    ? new GameModeStep("power", "TopSpeed24", "Güç planı değiştirilemedi", "Windows güç ayarlarından kontrol edin.", GameModeStepState.Failed)
                    : session.AppliedPowerPlan == null
                        ? new GameModeStep("power", "TopSpeed24", "Güç planına dokunulmadı", "Profilde \"Değiştirilmez\" seçili.", GameModeStepState.Skipped)
                        : new GameModeStep("power", "TopSpeed24", $"Güç planı: {session.AppliedPowerPlan}", "Önceki plan kaydedildi.", GameModeStepState.Applied));
            }
            else
            {
                steps.Add(keepPlan
                    ? new GameModeStep("power", "TopSpeed24", "Güç planına dokunulmaz", "Profilde \"Değiştirilmez\" seçili.", GameModeStepState.Skipped)
                    : new GameModeStep("power", "TopSpeed24", $"Güç planı: {PowerPlanName(profile.PowerPlan)}",
                        string.Equals(profile.PowerPlan, Ultimate, StringComparison.OrdinalIgnoreCase)
                            ? "Nihai plan yoksa Yüksek Performans kullanılır."
                            : "Önceki plan kaydedilir.",
                        GameModeStepState.WillApply));
            }

            // 2) Bakım'ın arka plan işleri — her zaman
            steps.Add(new GameModeStep("maintenance", "Pause24", "Bakım'ın arka plan işleri duraklar",
                "Otomatik bellek temizliği ve bakım denetimleri oyun sırasında çalışmaz.",
                active ? GameModeStepState.Applied : GameModeStepState.WillApply));

            // 3) Bellek
            if (!profile.TrimMemory)
                steps.Add(new GameModeStep("memory", "Ram20", "Arka plan belleği boşaltılmaz", "Profilde kapalı.", GameModeStepState.Skipped));
            else if (active)
                steps.Add(new GameModeStep("memory", "Ram20", "Arka plan belleği boşaltıldı",
                    session!.FreedBytes > 0 ? $"{Text.ByteFormatter.Format(session.FreedBytes)} Windows'a geri verildi." : "Geri verilecek kayda değer bellek yoktu.",
                    GameModeStepState.Applied));
            else
                steps.Add(new GameModeStep("memory", "Ram20", "Arka plan belleği boşaltılır",
                    "Kullanılmayan bellek Windows'a geri verilir; etkisi geçicidir.", GameModeStepState.WillApply));

            // 4) Askıya alınacak uygulamalar
            if (profile.SuspendApps.Count == 0)
                steps.Add(new GameModeStep("suspend", "PauseCircle24", "Uygulama askıya alınmaz", "Listeye uygulama eklenmedi.", GameModeStepState.Skipped));
            else if (active)
                steps.Add(session!.SuspendedApps.Count > 0
                    ? new GameModeStep("suspend", "PauseCircle24", $"{session.SuspendedApps.Count} uygulama askıda", string.Join(" · ", session.SuspendedApps), GameModeStepState.Applied)
                    : new GameModeStep("suspend", "PauseCircle24", "Askıya alınacak uygulama çalışmıyordu", string.Join(" · ", profile.SuspendApps), GameModeStepState.Skipped));
            else
                steps.Add(new GameModeStep("suspend", "PauseCircle24",
                    profile.SuspendApps.Count == 1 ? "1 uygulama askıya alınır" : $"{profile.SuspendApps.Count} uygulama askıya alınır",
                    string.Join(" · ", profile.SuspendApps), GameModeStepState.WillApply));

            return steps;
        }

        /// <summary>Uygulanacak (ya da uygulanan) adım sayısı.</summary>
        public static int ActiveStepCount(IEnumerable<GameModeStep> steps) =>
            steps.Count(s => s.State is GameModeStepState.WillApply or GameModeStepState.Applied);
    }
}
