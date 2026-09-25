using System;
using System.Collections.Generic;
using System.Linq;
using Bakım.Core.ServiceControl;
using Xunit;

namespace Bakim.Core.Tests;

public class ServicePolicyTests
{
    [Theory]
    [InlineData("RpcSs", true)]
    [InlineData("rpcss", true)]
    [InlineData("Spooler", false)]
    [InlineData(null, false)]
    public void Critical(string? name, bool expected) => Assert.Equal(expected, CriticalServicePolicy.IsCritical(name));

    [Fact]
    public void Profiles_NeverTouchCriticalServices()
    {
        foreach (var p in ServiceProfiles.All)
            Assert.DoesNotContain(p.Changes, c => CriticalServicePolicy.IsCritical(c.ServiceName) || CriticalServicePolicy.ReducesSecurityWhenDisabled(c.ServiceName));
        Assert.Equal(ServiceProfiles.All.Count, ServiceProfiles.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void Preview_SkipsMissingAndAlreadyRestricted()
    {
        var xbox = ServiceProfiles.All.Single(p => p.Id == "no-xbox");
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["XblAuthManager"] = "Auto",
            ["XblGameSave"] = "Manual",       // zaten hedefte
            ["XboxGipSvc"] = "Disabled",      // daha kısıtlı: dokunulmaz
            // XboxNetApiSvc yüklü değil
        };
        var steps = ServiceProfiles.Preview(xbox, current);
        var step = Assert.Single(steps);
        Assert.Equal("XblAuthManager", step.ServiceName);
        Assert.Equal("Otomatik", ServiceProfiles.ModeLabel(step.CurrentMode));
        Assert.Equal("El ile", ServiceProfiles.ModeLabel(step.TargetMode));
    }

    [Theory]
    [InlineData("20150101000000.******+000", "Realtek", true)]
    [InlineData("20240101", "Realtek", false)]
    [InlineData("20150101", "Microsoft", false)]
    [InlineData("2006-06-21", "Standart", false)]
    [InlineData("-", "Realtek", false)]
    public void OldDrivers(string date, string maker, bool old) =>
        Assert.Equal(old, DriverAge.IsOldThirdParty(date, maker, new DateTime(2026, 9, 25)));
}
