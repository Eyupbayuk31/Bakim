using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bakım.Models;
using Bakım.Services;
using Bakım.ViewModels;
using Xunit;

namespace Bakim.Tests;

public class SettingsShellIntegrationTests
{
    private sealed class MockShellContextMenuService : IShellContextMenuService
    {
        public bool IsRegistered { get; set; }
        public int RegisterCalls { get; private set; }
        public int UnregisterCalls { get; private set; }

        public bool IsContextMenuRegistered() => IsRegistered;

        public bool RegisterContextMenu()
        {
            RegisterCalls++;
            IsRegistered = true;
            return true;
        }

        public bool UnregisterContextMenu()
        {
            UnregisterCalls++;
            IsRegistered = false;
            return true;
        }

        public bool ToggleContextMenu()
        {
            if (IsRegistered)
                return !UnregisterContextMenu();
            else
                return RegisterContextMenu();
        }
    }

    private sealed class StubSettingsService : IAppSettingsService
    {
        public AppSettingsData Current { get; } = new();
        public string SettingsFilePath => "(test)";
        public event Action<AppSettingsData>? SettingsChanged;
        public AppSettingsData Load() => Current;
        public void Save(AppSettingsData data) => SettingsChanged?.Invoke(data);
        public void Update(Action<AppSettingsData> mutate) => mutate(Current);
    }

    private static (SettingsViewModel vm, MockShellContextMenuService shellMock) CreateViewModel(bool initiallyRegistered)
    {
        var shellMock = new MockShellContextMenuService { IsRegistered = initiallyRegistered };
        var services = new ServiceCollection();

        Bakım.App.ConfigureServices(services, NullLogService.Instance, new StubSettingsService());
        services.RemoveAll<IShellContextMenuService>();
        services.AddSingleton<IShellContextMenuService>(shellMock);

        var provider = services.BuildServiceProvider();
        var vm = provider.GetRequiredService<SettingsViewModel>();

        return (vm, shellMock);
    }

    [Fact]
    public void SettingsViewModel_InitialState_ReflectsShellContextMenuRegistration()
    {
        var (vm, _) = CreateViewModel(initiallyRegistered: true);

        Assert.True(vm.IsShellContextMenuEnabled);
        Assert.Equal("Kayıtlı ve Aktif", vm.ShellContextMenuStatus);
    }

    [Fact]
    public void SettingsViewModel_ToggleShellContextMenu_RegistersAndUnregistersCorrectly()
    {
        var (vm, shellMock) = CreateViewModel(initiallyRegistered: false);

        Assert.False(vm.IsShellContextMenuEnabled);
        Assert.Equal("Devre Dışı", vm.ShellContextMenuStatus);

        // Enable context menu
        vm.IsShellContextMenuEnabled = true;

        Assert.Equal(1, shellMock.RegisterCalls);
        Assert.True(vm.IsShellContextMenuEnabled);
        Assert.Equal("Kayıtlı ve Aktif", vm.ShellContextMenuStatus);

        // Disable context menu
        vm.IsShellContextMenuEnabled = false;

        Assert.Equal(1, shellMock.UnregisterCalls);
        Assert.False(vm.IsShellContextMenuEnabled);
        Assert.Equal("Devre Dışı", vm.ShellContextMenuStatus);
    }

    [Fact]
    public void SettingsViewModel_RefreshShellContextMenuStatus_UpdatesStateFromService()
    {
        var (vm, shellMock) = CreateViewModel(initiallyRegistered: false);

        Assert.False(vm.IsShellContextMenuEnabled);

        // Simulate external registry change
        shellMock.IsRegistered = true;
        vm.RefreshShellContextMenuStatusCommand.Execute(null);

        Assert.True(vm.IsShellContextMenuEnabled);
        Assert.Equal("Kayıtlı ve Aktif", vm.ShellContextMenuStatus);
    }
}
