using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakım.Helpers;
using Bakım.Models;
using Bakım.Services;
using Wpf.Ui.Controls;

namespace Bakım.ViewModels
{
    /// <summary>
    /// Windows Araçları (MASTER_PLAN §2.2, §5.17): yerleşik yönetim konsolları ve klasik araçlar.
    /// Mağaza yazılım yükler; mmc, regedit ve benzerlerini açmak ayrı bir iş olduğu için taşındı.
    /// </summary>
    public partial class WindowsToolsViewModel : ObservableObject, IModuleViewModel
    {
        private readonly IClassicAppsService _classicAppsService;
        private readonly ILogService _log;
        private readonly List<ClassicAppItem> _masterClassicTools = new();
        private bool _loaded;

        public WindowsToolsViewModel(IClassicAppsService classicAppsService, ILogService log)
        {
            _classicAppsService = classicAppsService;
            _log = log;
        }

        public async Task OnActivatedAsync()
        {
            if (!_loaded)
            {
                _loaded = true;
                await LoadClassicToolsAsync();
            }
            else
            {
                await RefreshClassicToolsStateAsync();
            }
        }

        public Task OnDeactivatedAsync() => Task.CompletedTask;

        #region Konsollar ve klasik araçlar

        public ObservableCollection<ClassicAppItem> ClassicTools { get; } = new();
        public ObservableCollection<ClassicAppItem> DisplayClassicTools { get; } = new();

        [ObservableProperty]
        private string _toolSearchQuery = string.Empty;

        [ObservableProperty]
        private string _selectedToolCategory = "Tümü";

        [ObservableProperty]
        private bool _isPhotoViewerActivated;

        [ObservableProperty]
        private int _classicToolsTotalCount;

        [ObservableProperty]
        private string _toolOperationMessage = string.Empty;

        [ObservableProperty]
        private bool _isToolOperationMessageOpen;

        [ObservableProperty]
        private InfoBarSeverity _toolOperationSeverity = InfoBarSeverity.Informational;

        partial void OnToolSearchQueryChanged(string value) => ApplyToolFilters();
        partial void OnSelectedToolCategoryChanged(string value) => ApplyToolFilters();

        [RelayCommand]
        public void SetToolCategory(string category)
        {
            SelectedToolCategory = category;
        }

        public async Task LoadClassicToolsAsync()
        {
            try
            {
                var tools = await _classicAppsService.GetClassicToolsAsync();
                _masterClassicTools.Clear();
                _masterClassicTools.AddRange(tools);

                ClassicTools.Clear();
                foreach (var tool in tools)
                {
                    ClassicTools.Add(tool);
                }

                ClassicToolsTotalCount = _masterClassicTools.Count;
                IsPhotoViewerActivated = await _classicAppsService.IsWindowsPhotoViewerActivatedAsync();

                ApplyToolFilters();
            }
            catch (Exception ex)
            {
                _log.Error("Klasik araçlar yüklenirken hata oluştu.", ex, nameof(WindowsToolsViewModel));
            }
        }

        public async Task RefreshClassicToolsStateAsync()
        {
            try
            {
                IsPhotoViewerActivated = await _classicAppsService.IsWindowsPhotoViewerActivatedAsync();
                var photoApp = ClassicTools.FirstOrDefault(t => t.Id == "photo_viewer");
                if (photoApp != null)
                {
                    photoApp.IsActivated = IsPhotoViewerActivated;
                }
            }
            catch (Exception ex)
            {
                _log.Error("Fotoğraf görüntüleyici durumu kontrol edilirken hata oluştu.", ex, nameof(WindowsToolsViewModel));
            }
        }

        private void ApplyToolFilters()
        {
            var query = _masterClassicTools.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SelectedToolCategory) && SelectedToolCategory != "Tümü")
            {
                query = query.Where(t => string.Equals(t.Category, SelectedToolCategory, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(ToolSearchQuery))
            {
                string q = ToolSearchQuery.Trim().ToLowerInvariant();
                query = query.Where(t =>
                    (t.Title?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.Description?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.ExecutablePath?.ToLowerInvariant().Contains(q) ?? false) ||
                    (t.Category?.ToLowerInvariant().Contains(q) ?? false));
            }

            DisplayClassicTools.Clear();
            foreach (var item in query)
            {
                DisplayClassicTools.Add(item);
            }
        }

        [RelayCommand]
        public async Task ActivateWindowsPhotoViewerAsync()
        {
            if (!UacHelper.IsAdministrator())
            {
                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi'ni etkinleştirmek için Yönetici yetkisi gereklidir.";
                ToolOperationSeverity = InfoBarSeverity.Warning;
                IsToolOperationMessageOpen = true;
                return;
            }

            bool ok = await _classicAppsService.ActivateWindowsPhotoViewerAsync();
            if (ok)
            {
                IsPhotoViewerActivated = true;
                var photoApp = ClassicTools.FirstOrDefault(t => t.Id == "photo_viewer");
                if (photoApp != null) photoApp.IsActivated = true;

                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi başarıyla tüm resim formatları (.jpg, .png, .bmp, .gif, .tiff) için sisteme tanımlandı ve varsayılan yapıldı!";
                ToolOperationSeverity = InfoBarSeverity.Success;
                IsToolOperationMessageOpen = true;

                _log.Info("Klasik Windows Fotoğraf Görüntüleyicisi başarıyla etkinleştirildi.", nameof(WindowsToolsViewModel));
            }
            else
            {
                ToolOperationMessage = "Klasik Windows Fotoğraf Görüntüleyicisi kayıt defterine tanımlanamadı.";
                ToolOperationSeverity = InfoBarSeverity.Error;
                IsToolOperationMessageOpen = true;
            }
        }

        [RelayCommand]
        public async Task LaunchClassicToolAsync(ClassicAppItem? app)
        {
            if (app == null) return;

            if (app.Id == "photo_viewer")
            {
                await ActivateWindowsPhotoViewerAsync();
                return;
            }

            bool ok = await _classicAppsService.LaunchClassicToolAsync(app.Id);
            if (!ok)
            {
                ToolOperationMessage = $"'{app.Title}' başlatılamadı. Konsolun Windows sürümünüzde mevcut olduğundan emin olun.";
                ToolOperationSeverity = InfoBarSeverity.Error;
                IsToolOperationMessageOpen = true;
            }
            else
            {
                ToolOperationMessage = $"'{app.Title}' başarıyla başlatıldı.";
                ToolOperationSeverity = InfoBarSeverity.Success;
                IsToolOperationMessageOpen = true;
            }
        }

        [RelayCommand]
        public void DismissToolOperationMessage()
        {
            IsToolOperationMessageOpen = false;
        }

        #endregion
    }
}
