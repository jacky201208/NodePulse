using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace NodePulse.Models
{
    public enum ModLoaderType
    {
        None,
        Forge,
        NeoForge,
        Fabric,
        Quilt,
        OptiFine
    }

    public partial class ModLoaderOption : ObservableObject
    {
        public ModLoaderType Type { get; set; }
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconBg { get; set; } = "#3A3855";
        public Bitmap? IconBitmap { get; set; }

        /// <summary>
        /// ★ 图标大小按加载器类型缩放：
        ///   原版：36（缩小）
        ///   OptiFine / Quilt：44（扩大）
        ///   Forge：38（略小）
        ///   其他：40（正常）
        /// </summary>
        public double IconSize => Type switch
        {
            ModLoaderType.None => 36,
            ModLoaderType.OptiFine => 44,
            ModLoaderType.Quilt => 44,
            ModLoaderType.Forge => 38,
            _ => 40
        };

        [ObservableProperty] private bool _isSelected;

        [ObservableProperty] private bool _isLoadingVersions;
        [ObservableProperty] private bool _versionsLoaded;
        [ObservableProperty] private string _loadingText = "正在读取版本...";

        public ObservableCollection<string> AvailableVersions { get; } = new();
        [ObservableProperty] private string _selectedVersion = "";

        [ObservableProperty] private bool _showApiSelector;
        [ObservableProperty] private bool _isLoadingApi;
        public ObservableCollection<string> AvailableApiVersions { get; } = new();
        [ObservableProperty] private string _selectedApiVersion = "";
        [ObservableProperty] private bool _installFabricApi = true;

        // ============ 显示控制 ============

        public bool IsVanilla => Type == ModLoaderType.None;
        public bool ShowVersionArea => !IsVanilla;
        public bool ShowApiRow => ShowApiSelector && IsSelected;
        public bool ShowVersionCombo => IsSelected && VersionsLoaded && !IsVanilla;
        public bool ShowVersionText => !IsSelected && VersionsLoaded && !IsVanilla;
        public bool ShowLoadError => !VersionsLoaded && !IsLoadingVersions && !IsVanilla;

        partial void OnIsSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowApiRow));
            OnPropertyChanged(nameof(ShowVersionCombo));
            OnPropertyChanged(nameof(ShowVersionText));
        }

        partial void OnVersionsLoadedChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowVersionCombo));
            OnPropertyChanged(nameof(ShowVersionText));
            OnPropertyChanged(nameof(ShowLoadError));
        }

        partial void OnIsLoadingVersionsChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowLoadError));
        }

        partial void OnShowApiSelectorChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowApiRow));
        }
    }

    public class ModLoaderConfig
    {
        public string BaseVersion { get; set; } = "";
        public string FinalVersionName { get; set; } = "";
        public ModLoaderType SelectedLoader { get; set; } = ModLoaderType.None;
        public string LoaderVersion { get; set; } = "";
        public bool InstallFabricApi { get; set; }
        public string FabricApiVersion { get; set; } = "";
    }
}