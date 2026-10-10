using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    public enum InstanceTabKind
    {
        Overview,
        Settings,
        Mods,
        Export
    }

    public partial class InstanceTabItem : ObservableObject
    {
        public InstanceTabKind Kind { get; set; }
        public string DisplayName { get; set; } = "";

        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isVisible = true;
    }

    public class SettingsViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        public event Action? ReturnRequested;

        // ================================================================
        // Tab
        // ================================================================

        public ObservableCollection<InstanceTabItem> Tabs { get; } = new();

        private InstanceTabKind _selectedTab = InstanceTabKind.Overview;
        public InstanceTabKind SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    OnPropertyChanged(nameof(IsOverviewTab));
                    OnPropertyChanged(nameof(IsSettingsTab));
                    OnPropertyChanged(nameof(IsModsTab));
                    OnPropertyChanged(nameof(IsExportTab));

                    if (value == InstanceTabKind.Mods)
                        _ = RefreshModListAsync();
                }
            }
        }

        public bool IsOverviewTab => SelectedTab == InstanceTabKind.Overview;
        public bool IsSettingsTab => SelectedTab == InstanceTabKind.Settings;
        public bool IsModsTab => SelectedTab == InstanceTabKind.Mods;
        public bool IsExportTab => SelectedTab == InstanceTabKind.Export;

        public RelayCommand<InstanceTabItem> SelectTabCommand { get; }

        private void SelectTab(InstanceTabItem? tab)
        {
            if (tab == null) return;
            foreach (var t in Tabs) t.IsSelected = (t == tab);
            SelectedTab = tab.Kind;
        }

        // ================================================================
        // 模组管理
        // ================================================================

        public ObservableCollection<ModInfo> ModList { get; } = new();

        private string _modStatusText = "";
        public string ModStatusText
        {
            get => _modStatusText;
            set => SetProperty(ref _modStatusText, value);
        }

        private string _modCountText = "";
        public string ModCountText
        {
            get => _modCountText;
            set => SetProperty(ref _modCountText, value);
        }

        public AsyncRelayCommand RefreshModListCommand { get; private set; }
        public AsyncRelayCommand<ModInfo> ToggleModCommand { get; private set; }
        public AsyncRelayCommand<ModInfo> DeleteModCommand { get; private set; }
        public RelayCommand OpenModsFolderCommand { get; private set; }
        public RelayCommand SelectAllModsCommand { get; private set; }
        public RelayCommand DeselectAllModsCommand { get; private set; }
        public AsyncRelayCommand EnableSelectedModsCommand { get; private set; }
        public AsyncRelayCommand DisableSelectedModsCommand { get; private set; }
        public AsyncRelayCommand DeleteSelectedModsCommand { get; private set; }

        private string ModsFolderPath => Path.Combine(
            VersionScanner.MinecraftFolder, "versions", SelectedVersionName, "mods");

        private async Task RefreshModListAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersionName)) return;

            ModStatusText = "正在扫描模组...";

            var folder = ModsFolderPath;
            var list = await Task.Run(() => ModInfo.Scan(folder));

            ModList.Clear();
            foreach (var m in list)
                ModList.Add(m);

            UpdateModCountText();
            ModStatusText = list.Count == 0
                ? "模组文件夹为空"
                : $"共 {list.Count} 个模组";
        }

        private void UpdateModCountText()
        {
            var total = ModList.Count;
            var enabled = ModList.Count(m => m.IsEnabled);
            var disabled = total - enabled;
            ModCountText = total == 0
                ? "暂无模组"
                : $"{total} 个模组（{enabled} 启用 / {disabled} 禁用）";
        }

        private async Task ToggleModAsync(ModInfo? mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(SelectedVersionName)) return;

            try
            {
                if (mod.IsEnabled)
                {
                    var newPath = mod.FullPath + ".disabled";
                    File.Move(mod.FullPath, newPath);

                    var idx = ModList.IndexOf(mod);
                    ModList.RemoveAt(idx);
                    var updated = new ModInfo
                    {
                        FileName = mod.FileName + ".disabled",
                        FullPath = newPath,
                        SizeBytes = mod.SizeBytes,
                        IsSelected = mod.IsSelected
                    };
                    ModList.Insert(idx, updated);
                }
                else
                {
                    var newPath = mod.FullPath[..^".disabled".Length];
                    File.Move(mod.FullPath, newPath);

                    var idx = ModList.IndexOf(mod);
                    ModList.RemoveAt(idx);
                    var updated = new ModInfo
                    {
                        FileName = Path.GetFileName(newPath),
                        FullPath = newPath,
                        SizeBytes = mod.SizeBytes,
                        IsSelected = mod.IsSelected
                    };
                    ModList.Insert(idx, updated);
                }

                UpdateModCountText();
                ModStatusText = mod.IsEnabled ? "已禁用" : "已启用";
            }
            catch (Exception ex)
            {
                ModStatusText = $"操作失败：{ex.Message}";
                await _dialogService.ShowMessageAsync("错误", ex.Message);
            }
        }

        private async Task DeleteModAsync(ModInfo? mod)
        {
            if (mod == null || string.IsNullOrWhiteSpace(SelectedVersionName)) return;

            var confirm = await _dialogService.ShowConfirmAsync(
                "删除模组",
                $"确定要删除模组 \"{mod.DisplayName}\" 吗？\n\n此操作不可恢复！");
            if (!confirm) return;

            try
            {
                if (File.Exists(mod.FullPath))
                    File.Delete(mod.FullPath);

                ModList.Remove(mod);
                UpdateModCountText();
                ModStatusText = "已删除";
            }
            catch (Exception ex)
            {
                ModStatusText = $"删除失败：{ex.Message}";
                await _dialogService.ShowMessageAsync("错误", ex.Message);
            }
        }

        private void OpenModsFolder()
        {
            try
            {
                var dir = ModsFolderPath;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                OpenFolderInExplorer(dir);
            }
            catch (Exception ex)
            {
                ModStatusText = $"打开失败：{ex.Message}";
            }
        }

        private void SelectAllMods()
        {
            foreach (var m in ModList) m.IsSelected = true;
        }

        private void DeselectAllMods()
        {
            foreach (var m in ModList) m.IsSelected = false;
        }

        private async Task EnableSelectedModsAsync()
        {
            var selected = ModList.Where(m => m.IsSelected && !m.IsEnabled).ToList();
            foreach (var m in selected)
                await ToggleModAsync(m);
        }

        private async Task DisableSelectedModsAsync()
        {
            var selected = ModList.Where(m => m.IsSelected && m.IsEnabled).ToList();
            foreach (var m in selected)
                await ToggleModAsync(m);
        }

        private async Task DeleteSelectedModsAsync()
        {
            var selected = ModList.Where(m => m.IsSelected).ToList();
            if (selected.Count == 0) return;

            var confirm = await _dialogService.ShowConfirmAsync(
                "批量删除模组",
                $"确定要删除选中的 {selected.Count} 个模组吗？\n\n此操作不可恢复！");
            if (!confirm) return;

            foreach (var m in selected)
            {
                try
                {
                    if (File.Exists(m.FullPath))
                        File.Delete(m.FullPath);
                    ModList.Remove(m);
                }
                catch { }
            }

            UpdateModCountText();
            ModStatusText = $"已删除 {selected.Count} 个模组";
        }

        // ================================================================
        // 实例信息
        // ================================================================

        private MinecraftInstance? _instance;
        public MinecraftInstance? Instance
        {
            get => _instance;
            set
            {
                if (SetProperty(ref _instance, value))
                {
                    OnPropertyChanged(nameof(InstanceName));
                    OnPropertyChanged(nameof(InstanceTypeText));
                    OnPropertyChanged(nameof(LoaderName));
                    OnPropertyChanged(nameof(InstanceSubtitle));
                    OnPropertyChanged(nameof(TotalSizeText));
                }
            }
        }

        public string InstanceName => Instance?.Name ?? SelectedVersionName;

        public string InstanceTypeText => Instance?.Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照",
            _ => Instance?.Type ?? ""
        };

        public string LoaderName => Instance?.LoaderType switch
        {
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            "optifine" => "OptiFine",
            _ => "原版"
        };

        public string InstanceSubtitle
        {
            get
            {
                var type = InstanceTypeText;
                var java = Instance?.DisplayJavaMajor ?? 0;
                var loader = LoaderName;
                var baseVer = Instance?.InheritsFrom;
                if (string.IsNullOrEmpty(baseVer)) baseVer = Instance?.Name ?? "";

                return $"{baseVer}  ·  {type}  ·  Java {java}  ·  {loader}";
            }
        }

        public string TotalSizeText => FormatSize(Instance?.TotalSize ?? 0);

        public int LaunchCount => _currentSettings.LaunchCount;

        public string LaunchCountText => LaunchCount <= 0
            ? "从未启动"
            : $"{LaunchCount} 次";

        // ================================================================
        // 当前实例设置
        // ================================================================

        private InstanceSettings _currentSettings = new();
        public InstanceSettings CurrentSettings
        {
            get => _currentSettings;
            set
            {
                if (SetProperty(ref _currentSettings, value))
                {
                    OnPropertyChanged(nameof(LaunchCount));
                    OnPropertyChanged(nameof(LaunchCountText));
                    SyncFromSettings();
                }
            }
        }

        private string _selectedVersionName = "";
        public string SelectedVersionName
        {
            get => _selectedVersionName;
            set => SetProperty(ref _selectedVersionName, value);
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ================================================================
        // 设置页 · Java
        // ================================================================

        public ObservableCollection<string> JavaModes { get; } = new()
        {
            "自动选择", "手动指定"
        };

        public string SelectedJavaMode
        {
            get => _currentSettings.UseAutoJava ? "自动选择" : "手动指定";
            set
            {
                if (string.IsNullOrEmpty(value)) return;

                bool isAuto = value == "自动选择";

                if (!isAuto && _currentSettings.UseAutoJava)
                {
                    if (_selectedJava == null && JavaList.Count > 0)
                    {
                        _selectedJava = JavaInfo.PickBest(JavaList, Instance?.JavaMajor ?? 8)
                                        ?? JavaList.FirstOrDefault();

                        if (_selectedJava != null)
                        {
                            _currentSettings.JavaPath = _selectedJava.JavaPath;
                            OnPropertyChanged(nameof(SelectedJava));
                        }
                    }
                }

                _currentSettings.UseAutoJava = isAuto;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAutoJava));
                OnPropertyChanged(nameof(IsManualJava));
            }
        }

        public bool IsAutoJava => _currentSettings.UseAutoJava;
        public bool IsManualJava => !_currentSettings.UseAutoJava;

        public ObservableCollection<JavaInfo> JavaList { get; } = new();

        private JavaInfo? _selectedJava;
        public JavaInfo? SelectedJava
        {
            get => _selectedJava;
            set
            {
                if (SetProperty(ref _selectedJava, value) && value != null)
                {
                    _currentSettings.JavaPath = value.JavaPath;
                    _currentSettings.UseAutoJava = false;

                    OnPropertyChanged(nameof(IsAutoJava));
                    OnPropertyChanged(nameof(IsManualJava));
                    OnPropertyChanged(nameof(SelectedJavaMode));
                }
            }
        }

        // ================================================================
        // 设置页 · 内存
        // ================================================================

        public bool IsMemoryModeGlobal
        {
            get => string.IsNullOrEmpty(_currentSettings.MemoryMode)
                   || _currentSettings.MemoryMode == "global";
            set { if (value) SetMemoryMode("global"); }
        }

        public bool IsMemoryModeAuto
        {
            get => _currentSettings.MemoryMode == "auto";
            set { if (value) SetMemoryMode("auto"); }
        }

        public bool IsMemoryModeCustom
        {
            get => _currentSettings.MemoryMode == "custom";
            set { if (value) SetMemoryMode("custom"); }
        }

        public bool IsCustomMemoryMode => _currentSettings.MemoryMode == "custom";

        private void SetMemoryMode(string mode)
        {
            _currentSettings.MemoryMode = mode;

            if (mode == "auto")
            {
                _customMemoryMB = AutoMemoryMB;
                _currentSettings.MaxMemoryMB = AutoMemoryMB;
                _currentSettings.MinMemoryMB = 512;
                OnPropertyChanged(nameof(CustomMemoryMB));
            }
            else if (mode == "custom")
            {
                _currentSettings.MaxMemoryMB = _customMemoryMB;
                _currentSettings.MinMemoryMB = Math.Min(512, _customMemoryMB / 4);
            }
            else
            {
                _currentSettings.MaxMemoryMB = 0;
                _currentSettings.MinMemoryMB = 0;
            }

            OnPropertyChanged(nameof(IsMemoryModeGlobal));
            OnPropertyChanged(nameof(IsMemoryModeAuto));
            OnPropertyChanged(nameof(IsMemoryModeCustom));
            OnPropertyChanged(nameof(IsCustomMemoryMode));
            OnPropertyChanged(nameof(MemoryAllocText));
        }

        private int _customMemoryMB = 2048;
        public int CustomMemoryMB
        {
            get => _customMemoryMB;
            set
            {
                if (SetProperty(ref _customMemoryMB, value))
                {
                    if (_currentSettings.MemoryMode == "custom")
                    {
                        _currentSettings.MaxMemoryMB = value;
                        _currentSettings.MinMemoryMB = Math.Min(512, value / 4);
                    }
                    OnPropertyChanged(nameof(MemoryAllocText));
                }
            }
        }

        public double SystemTotalGB
        {
            get
            {
                try
                {
                    return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes
                           / 1024.0 / 1024 / 1024;
                }
                catch { return 8.0; }
            }
        }

        public int AutoMemoryMB
        {
            get
            {
                double gb = SystemTotalGB / 4.0;
                if (gb > 8) gb = 8;
                if (gb < 1) gb = 1;
                return (int)(gb * 1024);
            }
        }

        public string MemoryInfoText =>
            $"{SystemTotalGB:F1} GB / {SystemTotalGB:F1} GB";

        public string MemoryAllocText
        {
            get
            {
                int mb;
                switch (_currentSettings.MemoryMode)
                {
                    case "auto": mb = AutoMemoryMB; break;
                    case "custom": mb = CustomMemoryMB; break;
                    default:
                        try { mb = GlobalSettings.Load().MaxMemoryMB; }
                        catch { mb = 2048; }
                        break;
                }
                return $"{mb / 1024.0:F1} GB";
            }
        }

        // ================================================================
        // 设置页 · 启动选项
        // ================================================================

        public bool InstanceIsolation
        {
            get => _currentSettings.InstanceIsolation;
            set
            {
                _currentSettings.InstanceIsolation = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> WindowTitleModes { get; } = new()
        {
            "跟随全局设置", "自定义", "使用默认标题"
        };

        public string SelectedWindowTitleMode
        {
            get => _currentSettings.WindowTitleMode switch
            {
                "custom" => "自定义",
                "default" => "使用默认标题",
                _ => "跟随全局设置"
            };
            set
            {
                _currentSettings.WindowTitleMode = value switch
                {
                    "自定义" => "custom",
                    "使用默认标题" => "default",
                    _ => "global"
                };
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCustomWindowTitle));
            }
        }

        public bool IsCustomWindowTitle =>
            _currentSettings.WindowTitleMode == "custom";

        public string CustomWindowTitle
        {
            get => _currentSettings.CustomWindowTitle;
            set { _currentSettings.CustomWindowTitle = value; OnPropertyChanged(); }
        }

        public string CustomInfo
        {
            get => _currentSettings.CustomInfo;
            set { _currentSettings.CustomInfo = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 设置页 · 服务器
        // ================================================================

        public bool OverrideAutoJoinServer
        {
            get => _currentSettings.OverrideAutoJoinServer;
            set { _currentSettings.OverrideAutoJoinServer = value; OnPropertyChanged(); }
        }

        public string AutoJoinServerOverride
        {
            get => _currentSettings.AutoJoinServerOverride;
            set { _currentSettings.AutoJoinServerOverride = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 设置页 · JVM / 游戏参数
        // ================================================================

        public string JvmArgs
        {
            get => _currentSettings.JvmArgs;
            set { _currentSettings.JvmArgs = value; OnPropertyChanged(); }
        }

        public string GameArgs
        {
            get => _currentSettings.GameArgs;
            set { _currentSettings.GameArgs = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 设置页 · 窗口
        // ================================================================

        public int WindowWidth
        {
            get => _currentSettings.WindowWidth;
            set { _currentSettings.WindowWidth = value; OnPropertyChanged(); }
        }

        public int WindowHeight
        {
            get => _currentSettings.WindowHeight;
            set { _currentSettings.WindowHeight = value; OnPropertyChanged(); }
        }

        public bool Fullscreen
        {
            get => _currentSettings.Fullscreen;
            set { _currentSettings.Fullscreen = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 导出页
        // ================================================================

        private string _exportName = "";
        public string ExportName
        {
            get => _exportName;
            set => SetProperty(ref _exportName, value);
        }

        private string _exportVersion = "1.0.0";
        public string ExportVersion
        {
            get => _exportVersion;
            set => SetProperty(ref _exportVersion, value);
        }

        private bool _exportIncludeGameJar = false;
        public bool ExportIncludeGameJar
        {
            get => _exportIncludeGameJar;
            set => SetProperty(ref _exportIncludeGameJar, value);
        }

        private bool _exportIncludeMods = true;
        public bool ExportIncludeMods
        {
            get => _exportIncludeMods;
            set => SetProperty(ref _exportIncludeMods, value);
        }

        private bool _exportIncludeSaves = false;
        public bool ExportIncludeSaves
        {
            get => _exportIncludeSaves;
            set => SetProperty(ref _exportIncludeSaves, value);
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set
            {
                if (SetProperty(ref _isExporting, value))
                    ExportCommand.RaiseCanExecuteChanged();
            }
        }

        private double _exportProgress;
        public double ExportProgress
        {
            get => _exportProgress;
            set => SetProperty(ref _exportProgress, value);
        }

        // ================================================================
        // Commands
        // ================================================================

        public AsyncRelayCommand RefreshJavaListCommand { get; }
        public RelayCommand SaveSettingsCommand { get; }
        public RelayCommand ReturnCommand { get; }

        public RelayCommand OpenInstanceFolderCommand { get; }
        public RelayCommand OpenSavesFolderCommand { get; }
        public AsyncRelayCommand RepairFilesCommand { get; }
        public AsyncRelayCommand ResetSettingsCommand { get; }
        public AsyncRelayCommand DeleteInstanceCommand { get; }

        public AsyncRelayCommand ExportCommand { get; }

        // ================================================================
        // 构造
        // ================================================================

        public SettingsViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            Tabs.Add(new InstanceTabItem
            {
                Kind = InstanceTabKind.Overview,
                DisplayName = "概览",
                IsSelected = true
            });
            Tabs.Add(new InstanceTabItem
            {
                Kind = InstanceTabKind.Settings,
                DisplayName = "设置"
            });
            Tabs.Add(new InstanceTabItem
            {
                Kind = InstanceTabKind.Mods,
                DisplayName = "模组"
            });
            Tabs.Add(new InstanceTabItem
            {
                Kind = InstanceTabKind.Export,
                DisplayName = "导出"
            });

            SelectTabCommand = new RelayCommand<InstanceTabItem>(SelectTab);
            RefreshJavaListCommand = new AsyncRelayCommand(RefreshJavaList);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ReturnCommand = new RelayCommand(() => ReturnRequested?.Invoke());

            OpenInstanceFolderCommand = new RelayCommand(OpenInstanceFolder);
            OpenSavesFolderCommand = new RelayCommand(OpenSavesFolder);
            RepairFilesCommand = new AsyncRelayCommand(RepairFilesAsync);
            ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
            DeleteInstanceCommand = new AsyncRelayCommand(DeleteInstanceAsync);

            ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsExporting);

            RefreshModListCommand = new AsyncRelayCommand(RefreshModListAsync);
            ToggleModCommand = new AsyncRelayCommand<ModInfo>(ToggleModAsync);
            DeleteModCommand = new AsyncRelayCommand<ModInfo>(DeleteModAsync);
            OpenModsFolderCommand = new RelayCommand(OpenModsFolder);
            SelectAllModsCommand = new RelayCommand(SelectAllMods);
            DeselectAllModsCommand = new RelayCommand(DeselectAllMods);
            EnableSelectedModsCommand = new AsyncRelayCommand(EnableSelectedModsAsync);
            DisableSelectedModsCommand = new AsyncRelayCommand(DisableSelectedModsAsync);
            DeleteSelectedModsCommand = new AsyncRelayCommand(DeleteSelectedModsAsync);
        }

        // ================================================================
        // 载入
        // ================================================================

        public async Task LoadDataAsync()
        {
            await RefreshJavaList();
        }

        public async Task SelectVersionAsync(string versionName)
        {
            SelectedVersionName = versionName;
            ExportName = versionName;

            MinecraftInstance? inst = null;
            try
            {
                inst = await Task.Run(() =>
                    MinecraftInstance.ScanAll()
                        .FirstOrDefault(i => i.Name == versionName));
            }
            catch { }

            if (inst != null)
            {
                try { inst.LoadIcon(); } catch { }
                Instance = inst;
            }

            CurrentSettings = InstanceSettings.Load(versionName);

            EnsureJavaSelectionValid();

            var hasLoader = !string.IsNullOrEmpty(inst?.LoaderType) && inst.LoaderType != "vanilla";
            var modsTab = Tabs.FirstOrDefault(t => t.Kind == InstanceTabKind.Mods);
            if (modsTab != null) modsTab.IsVisible = hasLoader;

            StatusText = $"当前实例：{versionName}";
        }

        private void EnsureJavaSelectionValid()
        {
            if (_currentSettings.UseAutoJava) return;
            if (JavaList.Count == 0) return;

            if (_selectedJava == null)
            {
                _selectedJava = JavaList.FirstOrDefault(j =>
                    string.Equals(j.JavaPath, _currentSettings.JavaPath,
                        StringComparison.OrdinalIgnoreCase));

                if (_selectedJava == null)
                    _selectedJava = JavaInfo.PickBest(JavaList, Instance?.JavaMajor ?? 8)
                                    ?? JavaList.FirstOrDefault();

                if (_selectedJava != null)
                {
                    _currentSettings.JavaPath = _selectedJava.JavaPath;
                    OnPropertyChanged(nameof(SelectedJava));
                }
            }
        }

        private void SyncFromSettings()
        {
            _customMemoryMB = _currentSettings.MaxMemoryMB > 0
                ? _currentSettings.MaxMemoryMB
                : AutoMemoryMB;

            OnPropertyChanged(nameof(SelectedJavaMode));
            OnPropertyChanged(nameof(IsAutoJava));
            OnPropertyChanged(nameof(IsManualJava));

            OnPropertyChanged(nameof(IsMemoryModeGlobal));
            OnPropertyChanged(nameof(IsMemoryModeAuto));
            OnPropertyChanged(nameof(IsMemoryModeCustom));
            OnPropertyChanged(nameof(IsCustomMemoryMode));
            OnPropertyChanged(nameof(CustomMemoryMB));
            OnPropertyChanged(nameof(MemoryAllocText));
            OnPropertyChanged(nameof(MemoryInfoText));
            OnPropertyChanged(nameof(AutoMemoryMB));

            OnPropertyChanged(nameof(InstanceIsolation));
            OnPropertyChanged(nameof(SelectedWindowTitleMode));
            OnPropertyChanged(nameof(IsCustomWindowTitle));
            OnPropertyChanged(nameof(CustomWindowTitle));
            OnPropertyChanged(nameof(CustomInfo));

            OnPropertyChanged(nameof(OverrideAutoJoinServer));
            OnPropertyChanged(nameof(AutoJoinServerOverride));

            OnPropertyChanged(nameof(JvmArgs));
            OnPropertyChanged(nameof(GameArgs));
            OnPropertyChanged(nameof(WindowWidth));
            OnPropertyChanged(nameof(WindowHeight));
            OnPropertyChanged(nameof(Fullscreen));

            if (!string.IsNullOrEmpty(_currentSettings.JavaPath))
            {
                _selectedJava = JavaList.FirstOrDefault(j =>
                    string.Equals(j.JavaPath, _currentSettings.JavaPath,
                        StringComparison.OrdinalIgnoreCase));
                OnPropertyChanged(nameof(SelectedJava));
            }
            else
            {
                _selectedJava = null;
                OnPropertyChanged(nameof(SelectedJava));
            }
        }

        private async Task RefreshJavaList()
        {
            StatusText = "正在扫描 Java...";

            var list = await Task.Run(() => JavaInfo.ScanAll());
            JavaList.Clear();
            foreach (var item in list)
                JavaList.Add(item);

            if (!string.IsNullOrEmpty(_currentSettings.JavaPath))
            {
                SelectedJava = JavaList.FirstOrDefault(j =>
                    string.Equals(j.JavaPath, _currentSettings.JavaPath,
                        StringComparison.OrdinalIgnoreCase));
            }

            EnsureJavaSelectionValid();

            StatusText = JavaList.Count == 0
                ? "未找到任何 Java"
                : $"共 {JavaList.Count} 个 Java";
        }

        // ================================================================
        // 保存
        // ================================================================

        private void SaveSettings()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersionName))
            {
                _ = _dialogService.ShowMessageAsync("错误", "未选择实例版本，无法保存设置。");
                return;
            }

            try
            {
                if (IsManualJava)
                {
                    _currentSettings.UseAutoJava = false;

                    if (SelectedJava != null)
                    {
                        _currentSettings.JavaPath = SelectedJava.JavaPath;
                    }
                    else if (string.IsNullOrEmpty(_currentSettings.JavaPath))
                    {
                        _currentSettings.UseAutoJava = true;
                        _currentSettings.JavaPath = "";
                    }
                }
                else
                {
                    _currentSettings.UseAutoJava = true;
                    _currentSettings.JavaPath = "";
                }

                _currentSettings.Save(SelectedVersionName);

                if (Instance != null && Instance.Name == SelectedVersionName)
                {
                    _ = ReloadInstanceAsync();
                }

                StatusText = "保存设置完成";
            }
            catch (Exception ex)
            {
                StatusText = "保存失败";
                _ = _dialogService.ShowMessageAsync("保存失败", ex.Message);
            }
        }

        private async Task ReloadInstanceAsync()
        {
            try
            {
                var name = SelectedVersionName;
                var inst = await Task.Run(() =>
                    MinecraftInstance.ScanAll()
                        .FirstOrDefault(i => i.Name == name));

                if (inst != null)
                {
                    try { inst.LoadIcon(); } catch { }
                    Instance = inst;
                }
            }
            catch { }
        }

        // ================================================================
        // 概览页 · 快捷操作
        // ================================================================

        private string InstanceFolder => Path.Combine(
            VersionScanner.MinecraftFolder, "versions", SelectedVersionName);

        private string SavesFolder => Path.Combine(InstanceFolder, "saves");

        private void OpenInstanceFolder()
        {
            try
            {
                var dir = InstanceFolder;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                OpenFolderInExplorer(dir);
            }
            catch (Exception ex)
            {
                StatusText = $"打开失败：{ex.Message}";
            }
        }

        private void OpenSavesFolder()
        {
            try
            {
                var dir = SavesFolder;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                OpenFolderInExplorer(dir);
            }
            catch (Exception ex)
            {
                StatusText = $"打开失败：{ex.Message}";
            }
        }

        private async Task RepairFilesAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersionName)) return;

            var confirm = await _dialogService.ShowConfirmAsync(
                "补全文件",
                $"确定要校验并补全 {SelectedVersionName} 的游戏文件吗？\n\n" +
                "缺失的文件会自动下载。");
            if (!confirm) return;

            StatusText = "正在校验游戏文件...";

            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    StatusText = p.Total > 0
                        ? $"{p.CurrentFile} ({p.Done}/{p.Total})"
                        : p.CurrentFile;
                });

                await Task.Run(async () =>
                {
                    await MinecraftLauncher.ValidateAndRepairAsync(
                        SelectedVersionName, progress);
                });

                StatusText = "文件补全完成";
            }
            catch (Exception ex)
            {
                StatusText = $"补全失败：{ex.Message}";
                await _dialogService.ShowMessageAsync("错误", ex.Message);
            }
        }

        private async Task ResetSettingsAsync()
        {
            var confirm = await _dialogService.ShowConfirmAsync(
                "重置设置",
                $"确定要把 {SelectedVersionName} 的实例设置恢复默认吗？\n\n" +
                "（不会影响存档和模组）");
            if (!confirm) return;

            CurrentSettings = new InstanceSettings();
            CurrentSettings.Save(SelectedVersionName);

            await ReloadInstanceAsync();

            StatusText = "已重置为默认设置";
        }

        private async Task DeleteInstanceAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersionName)) return;

            var confirm = await _dialogService.ShowConfirmAsync(
                "删除实例",
                $"确定要删除实例 \"{SelectedVersionName}\" 吗？\n\n" +
                $"将删除目录：\nversions\\{SelectedVersionName}\\\n\n" +
                "此操作不可恢复！");
            if (!confirm) return;

            try
            {
                var dir = InstanceFolder;
                if (Directory.Exists(dir))
                    await Task.Run(() => Directory.Delete(dir, recursive: true));

                StatusText = "已删除";
                await _dialogService.ShowMessageAsync("删除成功",
                    $"实例 \"{SelectedVersionName}\" 已删除。");
                ReturnRequested?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = $"删除失败：{ex.Message}";
                await _dialogService.ShowMessageAsync("删除失败", ex.Message);
            }
        }

        // ================================================================
        // 导出页
        // ================================================================

        private async Task ExportAsync()
        {
            if (string.IsNullOrWhiteSpace(ExportName))
            {
                StatusText = "请输入整合包名称";
                return;
            }

            var suggested = $"{ExportName}_{ExportVersion}.zip";
            var targetPath = await _dialogService.ShowSaveFileAsync(
                "导出整合包", suggested, "zip");

            if (string.IsNullOrEmpty(targetPath))
            {
                StatusText = "已取消";
                return;
            }

            IsExporting = true;
            ExportProgress = 0;
            StatusText = "正在导出...";

            try
            {
                var versionDir = InstanceFolder;
                if (!Directory.Exists(versionDir))
                    throw new Exception("实例目录不存在");

                var name = ExportName;
                var ver = ExportVersion;
                var includeJar = ExportIncludeGameJar;
                var includeMods = ExportIncludeMods;
                var includeSaves = ExportIncludeSaves;
                var versionName = SelectedVersionName;

                await Task.Run(() =>
                {
                    using var fs = new FileStream(targetPath,
                        FileMode.Create, FileAccess.Write);
                    using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

                    var jsonPath = Path.Combine(versionDir, versionName + ".json");
                    if (File.Exists(jsonPath))
                    {
                        zip.CreateEntryFromFile(
                            jsonPath,
                            $"versions/{versionName}/{versionName}.json");
                    }

                    var settingsPath = Path.Combine(versionDir, "instance-settings.json");
                    if (File.Exists(settingsPath))
                    {
                        zip.CreateEntryFromFile(
                            settingsPath,
                            $"versions/{versionName}/instance-settings.json");
                    }

                    if (includeMods)
                    {
                        var folders = new[]
                        {
                            "mods", "config", "resourcepacks",
                            "shaderpacks", "datapacks"
                        };
                        foreach (var folder in folders)
                        {
                            var folderPath = Path.Combine(versionDir, folder);
                            if (!Directory.Exists(folderPath)) continue;

                            foreach (var file in Directory.EnumerateFiles(
                                folderPath, "*", SearchOption.AllDirectories))
                            {
                                var rel = Path.GetRelativePath(versionDir, file);
                                zip.CreateEntryFromFile(
                                    file,
                                    $"versions/{versionName}/{rel}");
                            }
                        }
                    }

                    if (includeSaves)
                    {
                        var savesPath = Path.Combine(versionDir, "saves");
                        if (Directory.Exists(savesPath))
                        {
                            foreach (var file in Directory.EnumerateFiles(
                                savesPath, "*", SearchOption.AllDirectories))
                            {
                                var rel = Path.GetRelativePath(versionDir, file);
                                zip.CreateEntryFromFile(
                                    file,
                                    $"versions/{versionName}/{rel}");
                            }
                        }
                    }

                    if (includeJar)
                    {
                        var jarPath = Path.Combine(versionDir, versionName + ".jar");
                        if (File.Exists(jarPath))
                        {
                            zip.CreateEntryFromFile(
                                jarPath,
                                $"versions/{versionName}/{versionName}.jar");
                        }
                    }

                    var metaEntry = zip.CreateEntry("nodepulse-export.json");
                    using var writer = new StreamWriter(metaEntry.Open());
                    writer.Write(
                        "{\n" +
                        $"  \"name\": \"{EscapeJson(name)}\",\n" +
                        $"  \"version\": \"{EscapeJson(ver)}\",\n" +
                        $"  \"source\": \"{EscapeJson(versionName)}\",\n" +
                        $"  \"exportedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"\n" +
                        "}");
                });

                ExportProgress = 100;
                StatusText = $"已导出到 {targetPath}";
                await _dialogService.ShowMessageAsync("导出成功",
                    $"整合包已导出到：\n{targetPath}");
            }
            catch (Exception ex)
            {
                StatusText = $"导出失败：{ex.Message}";
                await _dialogService.ShowMessageAsync("导出失败", ex.Message);
            }
            finally
            {
                IsExporting = false;
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ================================================================
        // 工具
        // ================================================================

        private static void OpenFolderInExplorer(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", path);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", path);
                }
            }
            catch { }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024)
                return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }
    }
}