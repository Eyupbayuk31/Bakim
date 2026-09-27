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
    /// <param name="TrimMemory">v2: yalnızca bellek sıkışınca (≥ %85) ve yalnızca arka plan uygulamaları kırpılır.</param>
    /// <param name="Prioritize">Oyun "Normalin üstü", arka plan uygulamaları "Normalin altı" önceliğe alınır.</param>
    /// <param name="WindowsGameMode">Windows'un kendi Oyun Modu kapalıysa oturum boyunca açılır.</param>
    /// <param name="AutoLibrary">Otomatik başlatmada kütüphanede (Steam, Epic, Riot) bulunan oyunlar da izlenir.</param>
    public sealed record GameModeProfile(
        string PowerPlan,
        bool TrimMemory,
        IReadOnlyList<string> SuspendApps,
        bool AutoStart,
        IReadOnlyList<string> AutoStartGames,
        bool Prioritize = true,
        bool WindowsGameMode = true,
        bool AutoLibrary = true);

    /// <summary>Etkin bir oturumda gerçekten ne yapıldığı.</summary>
    /// <param name="FreedBytes">Bellek sıkışmasında arka plan uygulamalarından geri alınan bellek (anlık).</param>
    /// <param name="PrioritizedGame">Önceliği yükseltilen oyun süreci (bulunamadıysa null).</param>
    /// <param name="LoweredCount">Önceliği düşürülen arka plan süreci sayısı.</param>
    /// <param name="WindowsGameModeTurnedOn">Windows Oyun Modu bu oturumda açıldıysa true; zaten açıksa false; profilde kapalıysa null.</param>
    /// <param name="BaselineBackgroundCpu">Oyun Modu açılmadan hemen önceki arka plan işlemci yüzdesi.</param>
    /// <param name="SessionBackgroundCpu">Oturum boyunca arka plan işlemci ortalaması.</param>
    /// <param name="MemoryTrimSkipped">Bellek sıkışık olmadığı için kırpma yapılmadı.</param>
    public sealed record GameModeSessionInfo(
        DateTime StartedAtUtc,
        string? TriggerGame,
        string? AppliedPowerPlan,
        bool PowerPlanFailed,
        long FreedBytes,
        IReadOnlyList<string> SuspendedApps,
        int SuspendedProcessCount,
        string? PrioritizedGame = null,
        int LoweredCount = 0,
        bool? WindowsGameModeTurnedOn = null,
        double? BaselineBackgroundCpu = null,
        double? SessionBackgroundCpu = null,
        bool MemoryTrimSkipped = false)
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

            // 3) Windows Oyun Modu
            if (!profile.WindowsGameMode)
                steps.Add(new GameModeStep("wingame", "Games24", "Windows Oyun Modu'na dokunulmaz", "Profilde kapalı.", GameModeStepState.Skipped));
            else if (active)
                steps.Add(session!.WindowsGameModeTurnedOn == true
                    ? new GameModeStep("wingame", "Games24", "Windows Oyun Modu açıldı", "Kapatınca önceki ayar geri yüklenir.", GameModeStepState.Applied)
                    : new GameModeStep("wingame", "Games24", "Windows Oyun Modu zaten açıktı", "Oyuna öncelik verir, oyun sırasında güncelleme kurulumunu erteler.", GameModeStepState.Applied));
            else
                steps.Add(new GameModeStep("wingame", "Games24", "Windows Oyun Modu açık tutulur",
                    "Kapalıysa oturum boyunca açılır: oyuna öncelik verir, oyun sırasında güncelleme kurulumunu erteler.", GameModeStepState.WillApply));

            // 4) Önceliklendirme
            if (!profile.Prioritize)
                steps.Add(new GameModeStep("priority", "ArrowSortUp24", "Öncelik değiştirilmez", "Profilde kapalı.", GameModeStepState.Skipped));
            else if (active)
                steps.Add(session!.PrioritizedGame is { } game
                    ? new GameModeStep("priority", "ArrowSortUp24", $"{game} önceliği yükseltildi",
                        session.LoweredCount > 0 ? $"{session.LoweredCount} arka plan süreci düşük önceliğe alındı." : "Düşürülecek arka plan uygulaması çalışmıyordu.",
                        GameModeStepState.Applied)
                    : new GameModeStep("priority", "ArrowSortUp24", "Oyun bekleniyor",
                        "Listedeki ya da kütüphanedeki bir oyun başlayınca önceliği yükseltilir.", GameModeStepState.WillApply));
            else
                steps.Add(new GameModeStep("priority", "ArrowSortUp24", "Oyun öne alınır",
                    "Oyun \"Normalin üstü\", arka plan uygulamaları \"Normalin altı\" önceliğe alınır; kapatınca eski hâline döner.", GameModeStepState.WillApply));

            // 5) Bellek: yalnızca sıkışmada ve yalnızca arka plan uygulamaları (v1 her şeyi kırpıyordu)
            if (!profile.TrimMemory)
                steps.Add(new GameModeStep("memory", "Ram20", "Bellek kırpılmaz", "Profilde kapalı.", GameModeStepState.Skipped));
            else if (active)
                steps.Add(session!.MemoryTrimSkipped
                    ? new GameModeStep("memory", "Ram20", "Bellek yeterliydi", "Kırpmaya gerek olmadı.", GameModeStepState.Skipped)
                    : new GameModeStep("memory", "Ram20", "Arka plan uygulamaları kırpıldı",
                        session.FreedBytes > 0 ? $"{Text.ByteFormatter.Format(session.FreedBytes)} oyuna bırakıldı." : "Geri alınacak kayda değer bellek yoktu.",
                        GameModeStepState.Applied));
            else
                steps.Add(new GameModeStep("memory", "Ram20", "Bellek sıkışırsa arka plan kırpılır",
                    $"Bellek %{GameModeRules.MemoryPressureThreshold} üstündeyse; oyuna ve ön plandaki pencereye dokunulmaz.", GameModeStepState.WillApply));

            // 6) Askıya alınacak uygulamalar
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
