using System;
using System.IO;
using Bakım.Core.Activity;
using Bakım.Services;
using Bakım.Services.Activity;
using Bakım.ViewModels;

namespace Bakim.Tests;

/// <summary>Testlerde çok bağımlılıklı ViewModel'leri kurmak için ortak fabrikalar.</summary>
internal static class TestFactories
{
    public static IActivityService Activity() => new ActivityService(
        new ActivityStore(Path.Combine(Path.GetTempPath(), "bakim-test-activity-" + Guid.NewGuid().ToString("N"))),
        Array.Empty<IUndoHandler>());

    public static StorageViewModel Storage(IDuplicateFinderService duplicates, ISystemInfoService info) =>
        new(duplicates, info, new DiskMapService(), new Bakım.Services.Safety.SafeDeleteService(NullLogService.Instance), Activity());
}
