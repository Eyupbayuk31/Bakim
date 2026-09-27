using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakım.Services;
using Bakım.Services.GameMode;
using Xunit;

namespace Bakim.Tests;

/// <summary>
/// Gerçek Windows uygulaması: yalnızca okuma ve TEST SÜRECİNİN KENDİSİ üzerinde değişiklik. Güç planına,
/// Windows Oyun Modu ayarına ve durum dosyasına dokunulmaz.
/// </summary>
public class GameModeSystemWindowsTests
{
    private readonly WindowsGameModeSystem _system = new(NullLogService.Instance);

    [Fact]
    public void BusyTime_Advances_AndCoresKnown()
    {
        var a = _system.GetSystemBusyTime();
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 200) { Thread.SpinWait(1000); }
        var b = _system.GetSystemBusyTime();
        Assert.True(b > a);
        Assert.True(_system.LogicalCores >= 1);
    }

    [Fact]
    public void Memory_And_Processes_AreRead()
    {
        var (load, available) = _system.GetMemoryStatus();
        Assert.InRange(load, 1u, 100u);
        Assert.True(available > 0);
        Assert.Contains(_system.GetRunningProcesses(), p => p.Pid == Environment.ProcessId);
        Assert.NotNull(_system.GetProcessCpuTime(Environment.ProcessId));
        Assert.NotNull(_system.GetStartTime(Environment.ProcessId));
    }

    [Fact]
    public void Priority_OnOwnProcess_SetAndRestore()
    {
        int pid = Environment.ProcessId;
        var original = _system.GetPriority(pid);
        Assert.NotNull(original);
        try
        {
            Assert.True(_system.SetPriority(pid, ProcessPriorityClass.BelowNormal));
            Assert.Equal(ProcessPriorityClass.BelowNormal, _system.GetPriority(pid));
        }
        finally
        {
            _system.SetPriority(pid, original!.Value);
        }
        Assert.Equal(original, _system.GetPriority(pid));
    }

    [Fact]
    public void Trim_OwnProcess_Succeeds_AndCriticalIsProtected()
    {
        Assert.True(_system.TrimWorkingSet(Environment.ProcessId));
        var csrss = Process.GetProcessesByName("csrss").FirstOrDefault();
        if (csrss != null) Assert.True(_system.IsProtected(csrss.Id, "csrss"));
        Assert.False(_system.IsProtected(99999999, "Discord"));
    }

    [Fact]
    public void WindowsGameMode_IsReadable() => Assert.InRange(_system.GetWindowsGameMode(), -1, 1);
}
