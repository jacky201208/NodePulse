using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    public enum SettingsTabKind
    {
        General,
        Download,
        Language,
        LaunchMemory,
        Java,
        Theme,
        Help
    }

    public class SettingsTabItem : INotifyPropertyChanged
    {
        public SettingsTabKind Kind { get; set; }
        public string DisplayName { get; set; } = "";
        public Avalonia.Media.Imaging.Bitmap? IconBitmap { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this,
                    new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class GlobalSettingsViewModel : INotifyPropertyChanged
    {
        private readonly IDialogService _dialogService;

        // ================================================================
        // Tab
        // ================================================================

        public ObservableCollection<SettingsTabItem> Tabs { get; } = new();

        private SettingsTabKind _selectedTab = SettingsTabKind.General;
        public SettingsTabKind SelectedTab
        {
            get => _selectedTab;
            set
            {
                _selectedTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsGeneralTab));
                OnPropertyChanged(nameof(IsDownloadTab));
                OnPropertyChanged(nameof(IsLanguageTab));
                OnPropertyChanged(nameof(IsLaunchMemoryTab));
                OnPropertyChanged(nameof(IsJavaTab));
                OnPropertyChanged(nameof(IsThemeTab));
                OnPropertyChanged(nameof(IsHelpTab));
            }
        }

        public bool IsGeneralTab => SelectedTab == SettingsTabKind.General;
        public bool IsDownloadTab => SelectedTab == SettingsTabKind.Download;
        public bool IsLanguageTab => SelectedTab == SettingsTabKind.Language;
        public bool IsLaunchMemoryTab => SelectedTab == SettingsTabKind.LaunchMemory;
        public bool IsJavaTab => SelectedTab == SettingsTabKind.Java;
        public bool IsThemeTab => SelectedTab == SettingsTabKind.Theme;
        public bool IsHelpTab => SelectedTab == SettingsTabKind.Help;

        public RelayCommand<SettingsTabItem> SelectTabCommand { get; }
        public RelayCommand<string> OpenUrlCommand { get; }

        // ================================================================
        // 通用
        // ================================================================

        private string _mcFolder = "";
        public string McFolder
        {
            get => _mcFolder;
            set { _mcFolder = value; OnPropertyChanged(); }
        }

        public string LauncherDataDir =>
            Path.Combine(AppContext.BaseDirectory, ".NodePulse");

        // ================================================================
        // 缓存管理
        // ================================================================

        private string _cacheSizeText = "计算中...";
        public string CacheSizeText
        {
            get => _cacheSizeText;
            set { _cacheSizeText = value; OnPropertyChanged(); }
        }

        private bool _isCacheBusy;
        public bool IsCacheBusy
        {
            get => _isCacheBusy;
            set
            {
                _isCacheBusy = value;
                OnPropertyChanged();
                ClearCacheCommand.RaiseCanExecuteChanged();
            }
        }

        // ================================================================
        // 下载
        // ================================================================

        public ObservableCollection<string> DownloadSources { get; } = new()
        {
            "官方源优先",
            "镜像优先"
        };

        private string _selectedDownloadSource = "官方源优先";
        public string SelectedDownloadSource
        {
            get => _selectedDownloadSource;
            set
            {
                _selectedDownloadSource = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseMirror));
            }
        }

        public bool UseMirror => SelectedDownloadSource == "镜像优先";

        private int _downloadThreadCount = 64;
        public int DownloadThreadCount
        {
            get => _downloadThreadCount;
            set { _downloadThreadCount = value; OnPropertyChanged(); }
        }

        private int _limitDownloadSpeed = 0;
        public int LimitDownloadSpeed
        {
            get => _limitDownloadSpeed;
            set { _limitDownloadSpeed = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 语言（只剩游戏内语言）
        // ================================================================

        public ObservableCollection<string> GameLanguageOptions { get; } = new()
        {
            "简体中文",
            "繁體中文",
            "English (US)",
            "日本語",
            "한국어",
            "Français",
            "Deutsch",
            "Español",
            "Русский",
            "Português (Brasil)",
            "Italiano",
            "Polski",
        };

        private string _selectedGameLanguage = "简体中文";
        public string SelectedGameLanguage
        {
            get => _selectedGameLanguage;
            set { _selectedGameLanguage = value; OnPropertyChanged(); }
        }

        private static string DisplayToCode(string display)
        {
            return display switch
            {
                "简体中文" => "zh_CN",
                "繁體中文" => "zh_TW",
                "English (US)" => "en_US",
                "日本語" => "ja_JP",
                "한국어" => "ko_KR",
                "Français" => "fr_FR",
                "Deutsch" => "de_DE",
                "Español" => "es_ES",
                "Русский" => "ru_RU",
                "Português (Brasil)" => "pt_BR",
                "Italiano" => "it_IT",
                "Polski" => "pl_PL",
                _ => "zh_CN"
            };
        }

        private static string CodeToDisplay(string code)
        {
            return code switch
            {
                "zh_CN" => "简体中文",
                "zh_TW" => "繁體中文",
                "en_US" => "English (US)",
                "ja_JP" => "日本語",
                "ko_KR" => "한국어",
                "fr_FR" => "Français",
                "de_DE" => "Deutsch",
                "es_ES" => "Español",
                "ru_RU" => "Русский",
                "pt_BR" => "Português (Brasil)",
                "it_IT" => "Italiano",
                "pl_PL" => "Polski",
                _ => "简体中文"
            };
        }

        // ================================================================
        // 启动与内存
        // ================================================================

        private bool _checkFileIntegrity = true;
        public bool CheckFileIntegrity
        {
            get => _checkFileIntegrity;
            set { _checkFileIntegrity = value; OnPropertyChanged(); }
        }

        private bool _autoRepairMissing = true;
        public bool AutoRepairMissing
        {
            get => _autoRepairMissing;
            set { _autoRepairMissing = value; OnPropertyChanged(); }
        }

        private bool _minimizeOnLaunch = false;
        public bool MinimizeOnLaunch
        {
            get => _minimizeOnLaunch;
            set { _minimizeOnLaunch = value; OnPropertyChanged(); }
        }

        private bool _fullscreenLaunch = false;
        public bool FullscreenLaunch
        {
            get => _fullscreenLaunch;
            set { _fullscreenLaunch = value; OnPropertyChanged(); }
        }

        private string _autoJoinServer = "";
        public string AutoJoinServer
        {
            get => _autoJoinServer;
            set { _autoJoinServer = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> MemoryModes { get; } = new()
        {
            "自动分配",
            "手动指定"
        };

        private string _selectedMemoryMode = "自动分配";
        public string SelectedMemoryMode
        {
            get => _selectedMemoryMode;
            set
            {
                _selectedMemoryMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsAutoMemoryMode));
                OnPropertyChanged(nameof(IsManualMemoryMode));
            }
        }

        public bool IsAutoMemoryMode => SelectedMemoryMode == "自动分配";
        public bool IsManualMemoryMode => SelectedMemoryMode == "手动指定";

        private int _maxMemory = 2048;
        public int MaxMemory
        {
            get => _maxMemory;
            set { _maxMemory = value; OnPropertyChanged(); }
        }

        private int _minMemory = 512;
        public int MinMemory
        {
            get => _minMemory;
            set { _minMemory = value; OnPropertyChanged(); }
        }

        public string SystemMemoryInfo
        {
            get
            {
                try
                {
                    var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    double totalGB = totalBytes / 1024.0 / 1024 / 1024;
                    return $"{totalGB:F1} GB / {totalGB:F1} GB";
                }
                catch { return "—"; }
            }
        }

        public string AutoMemoryText
        {
            get
            {
                try
                {
                    var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                    double totalGB = totalBytes / 1024.0 / 1024 / 1024;
                    double auto = totalGB / 4.0;
                    if (auto > 8.0) auto = 8.0;
                    if (auto < 1.0) auto = 1.0;
                    return $"{auto:F1} GB";
                }
                catch { return "—"; }
            }
        }

        // ================================================================
        // Java
        // ================================================================

        public ObservableCollection<string> JavaSelectionModes { get; } = new()
        {
            "自动选择",
            "手动指定"
        };

        private string _selectedJavaMode = "自动选择";
        public string SelectedJavaMode
        {
            get => _selectedJavaMode;
            set { _selectedJavaMode = value; OnPropertyChanged(); }
        }

        public ObservableCollection<JavaInfo> JavaList { get; } = new();

        private JavaInfo? _selectedJava;
        public JavaInfo? SelectedJava
        {
            get => _selectedJava;
            set
            {
                _selectedJava = value;
                OnPropertyChanged();

                if (value != null)
                {
                    GlobalJavaPath = value.JavaPath;
                    SelectedJavaMode = "手动指定";
                }
            }
        }

        private string _globalJavaPath = "";
        public string GlobalJavaPath
        {
            get => _globalJavaPath;
            set { _globalJavaPath = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 主题
        // ================================================================

        private bool _isDarkTheme = true;

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            private set
            {
                if (_isDarkTheme == value) return;
                _isDarkTheme = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLightTheme));
            }
        }

        public bool IsLightTheme => !_isDarkTheme;

        public RelayCommand SetDarkThemeCommand { get; }
        public RelayCommand SetLightThemeCommand { get; }

        private void SetDarkTheme()
        {
            if (IsDarkTheme) return;

            IsDarkTheme = true;
            ThemeManager.ApplyAndSave(ThemeManager.DarkTheme);
            StatusText = "已切换到深色模式";
        }

        private void SetLightTheme()
        {
            if (IsLightTheme) return;

            IsDarkTheme = false;
            ThemeManager.ApplyAndSave(ThemeManager.LightTheme);
            StatusText = "已切换到浅色模式";
        }

        // ================================================================
        // 帮助
        // ================================================================

        public string AppVersion => "NodePulse 1.0.0";
        public string AppAuthor => "NodePulse 项目组";

        // ================================================================
        // 状态
        // ================================================================

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }

        // ================================================================
        // Commands
        // ================================================================

        public AsyncRelayCommand BrowseMcFolderCommand { get; }
        public AsyncRelayCommand BrowseJavaCommand { get; }
        public AsyncRelayCommand RefreshJavaListCommand { get; }
        public AsyncRelayCommand SaveCommand { get; }
        public RelayCommand OpenLauncherDirCommand { get; }
        public RelayCommand ClearLogsCommand { get; }

        // ★ 缓存相关
        public AsyncRelayCommand ClearCacheCommand { get; }
        public RelayCommand OpenCacheFolderCommand { get; }
        public AsyncRelayCommand RefreshCacheSizeCommand { get; }

        public GlobalSettingsViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            BrowseMcFolderCommand = new AsyncRelayCommand(BrowseMcFolder);
            BrowseJavaCommand = new AsyncRelayCommand(BrowseJava);

            RefreshJavaListCommand = new AsyncRelayCommand(
                () => RefreshJavaList(forceRefresh: true));

            SaveCommand = new AsyncRelayCommand(SaveSettings, () => !IsLoading);
            OpenLauncherDirCommand = new RelayCommand(OpenLauncherDir);
            ClearLogsCommand = new RelayCommand(ClearLogs);

            SetDarkThemeCommand = new RelayCommand(SetDarkTheme);
            SetLightThemeCommand = new RelayCommand(SetLightTheme);

            SelectTabCommand = new RelayCommand<SettingsTabItem>(SelectTab);
            OpenUrlCommand = new RelayCommand<string>(OpenUrl);

            // ★ 缓存相关
            ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsCacheBusy);
            OpenCacheFolderCommand = new RelayCommand(OpenCacheFolder);
            RefreshCacheSizeCommand = new AsyncRelayCommand(RefreshCacheSizeAsync);

            InitTabs();
        }

        private static Avalonia.Media.Imaging.Bitmap? LoadSettingsIcon(string fileName)
        {
            try
            {
                var assemblyName = System.Reflection.Assembly
                    .GetExecutingAssembly().GetName().Name ?? "NodePulse";

                var uri = new Uri($"avares://{assemblyName}/Assets/Icons/Settings/{fileName}");

                if (!Avalonia.Platform.AssetLoader.Exists(uri))
                    return null;

                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private void InitTabs()
        {
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.General,
                DisplayName = "通用",
                IconBitmap = LoadSettingsIcon("general.png"),
                IsSelected = true
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.Download,
                DisplayName = "下载",
                IconBitmap = LoadSettingsIcon("download.png")
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.Language,
                DisplayName = "语言",
                IconBitmap = LoadSettingsIcon("language.png")
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.LaunchMemory,
                DisplayName = "启动和内存",
                IconBitmap = LoadSettingsIcon("memory.png")
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.Java,
                DisplayName = "Java",
                IconBitmap = LoadSettingsIcon("java.png")
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.Theme,
                DisplayName = "主题",
                IconBitmap = LoadSettingsIcon("theme.png")
            });
            Tabs.Add(new SettingsTabItem
            {
                Kind = SettingsTabKind.Help,
                DisplayName = "帮助与反馈",
                IconBitmap = LoadSettingsIcon("help.png")
            });
        }

        private void SelectTab(SettingsTabItem? tab)
        {
            if (tab == null) return;
            foreach (var t in Tabs) t.IsSelected = (t == tab);
            SelectedTab = tab.Kind;

            if (tab.Kind == SettingsTabKind.Java && JavaList.Count == 0)
                _ = RefreshJavaList(forceRefresh: false);

            // ★ 切到通用页时刷新缓存大小
            if (tab.Kind == SettingsTabKind.General)
                _ = RefreshCacheSizeAsync();
        }

        private void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", url);
                else if (OperatingSystem.IsLinux())
                    Process.Start("xdg-open", url);
            }
            catch (Exception ex)
            {
                StatusText = $"打开链接失败：{ex.Message}";
            }
        }

        // ================================================================
        // 载入 / 保存
        // ================================================================

        public void LoadCurrent()
        {
            McFolder = VersionScanner.MinecraftFolder;
            var g = GlobalSettings.Load();

            GlobalJavaPath = g.JavaPath;
            SelectedJavaMode = string.IsNullOrEmpty(g.JavaPath) ? "自动选择" : "手动指定";

            SelectedMemoryMode = g.MemoryMode == "manual" ? "手动指定" : "自动分配";
            MaxMemory = g.MaxMemoryMB > 0 ? g.MaxMemoryMB : 2048;
            MinMemory = g.MinMemoryMB > 0 ? g.MinMemoryMB : 512;

            SelectedDownloadSource = g.UseMirror ? "镜像优先" : "官方源优先";
            DownloadThreadCount = g.DownloadThreadCount switch
            {
                < 1 => 32,
                > 256 => 256,
                _ => g.DownloadThreadCount
            };
            LimitDownloadSpeed = g.LimitDownloadSpeedMB < 0 ? 0 : g.LimitDownloadSpeedMB;

            // 游戏内语言
            SelectedGameLanguage = CodeToDisplay(
                string.IsNullOrEmpty(g.GameLanguage) ? "zh_CN" : g.GameLanguage);

            CheckFileIntegrity = g.CheckFileIntegrity;
            AutoRepairMissing = g.AutoRepairMissing;
            MinimizeOnLaunch = g.MinimizeOnLaunch;
            FullscreenLaunch = g.FullscreenLaunch;
            AutoJoinServer = g.AutoJoinServer ?? "";

            _isDarkTheme = ThemeManager.IsDark;
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(IsLightTheme));

            // ★ 初始化时算一次缓存大小
            _ = RefreshCacheSizeAsync();

            StatusText = "";
        }

        private async Task SaveSettings()
        {
            IsLoading = true;
            StatusText = "正在保存...";
            try
            {
                var g = GlobalSettings.Load();

                g.JavaPath = GlobalJavaPath?.Trim() ?? "";
                g.UseAutoJava = string.IsNullOrEmpty(g.JavaPath);

                g.MemoryMode = IsManualMemoryMode ? "manual" : "auto";
                g.MaxMemoryMB = MaxMemory > 0 ? MaxMemory : 2048;
                g.MinMemoryMB = MinMemory > 0 ? MinMemory : 512;

                g.UseMirror = UseMirror;
                g.MinecraftFolder = McFolder?.Trim() ?? "";
                g.Theme = ThemeManager.CurrentTheme;

                // 游戏内语言
                g.GameLanguage = DisplayToCode(SelectedGameLanguage);

                g.DownloadThreadCount = DownloadThreadCount switch
                {
                    < 1 => 1,
                    > 256 => 256,
                    _ => DownloadThreadCount
                };

                g.LimitDownloadSpeedMB = LimitDownloadSpeed < 0 ? 0 : LimitDownloadSpeed;

                g.CheckFileIntegrity = CheckFileIntegrity;
                g.AutoRepairMissing = AutoRepairMissing;
                g.MinimizeOnLaunch = MinimizeOnLaunch;
                g.FullscreenLaunch = FullscreenLaunch;
                g.AutoJoinServer = AutoJoinServer?.Trim() ?? "";

                await Task.Run(() =>
                {
                    g.Save();
                    VersionDownloader.UseMirror = g.UseMirror;
                    if (!string.IsNullOrEmpty(g.MinecraftFolder))
                    {
                        Directory.CreateDirectory(g.MinecraftFolder);
                        VersionScanner.MinecraftFolder = g.MinecraftFolder;
                    }
                });

                StatusText = $"已保存（下载线程数：{g.DownloadThreadCount}）";
            }
            catch (Exception ex)
            {
                StatusText = "保存失败";
                await _dialogService.ShowMessageAsync("错误", $"保存失败：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ================================================================
        // Java 相关
        // ================================================================

        private async Task RefreshJavaList(bool forceRefresh)
        {
            try
            {
                StatusText = forceRefresh ? "正在重新扫描 Java..." : "正在加载 Java 列表...";

                var list = await Task.Run(() => JavaInfo.ScanAll(forceRefresh));

                JavaList.Clear();
                foreach (var j in list)
                    JavaList.Add(j);

                if (!string.IsNullOrEmpty(GlobalJavaPath))
                {
                    var match = JavaList.FirstOrDefault(j =>
                        string.Equals(j.JavaPath, GlobalJavaPath,
                            StringComparison.OrdinalIgnoreCase));
                    if (match != null) _selectedJava = match;
                }

                OnPropertyChanged(nameof(SelectedJava));

                StatusText = list.Count == 0
                    ? "未找到任何 Java，请点「导入」手动添加"
                    : $"共 {list.Count} 个 Java";
            }
            catch (Exception ex)
            {
                StatusText = $"扫描失败：{ex.Message}";
            }
        }

        private async Task BrowseMcFolder()
        {
            var folder = await _dialogService.PickFolderAsync("选择 .minecraft 文件夹");
            if (!string.IsNullOrEmpty(folder))
                McFolder = folder;
        }

        private async Task BrowseJava()
        {
            var file = await _dialogService.PickFileAsync(
                "选择 java.exe 或 javaw.exe",
                new[] { "*.exe", "*" });

            if (string.IsNullOrEmpty(file)) return;

            var info = await Task.Run(() => JavaInfo.Probe(file));

            if (info == null)
            {
                StatusText = "无法识别该 Java 的版本信息";
                return;
            }

            var existing = JavaList.FirstOrDefault(j =>
                string.Equals(j.JavaPath, info.JavaPath,
                    StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                JavaList.Insert(0, info);
                JavaInfo.AppendToCache(info);
            }

            SelectedJava = existing ?? info;
            GlobalJavaPath = info.JavaPath;
            SelectedJavaMode = "手动指定";

            StatusText = $"已添加：Java {info.MajorVersion} ({info.FullVersion})";
        }

        private void OpenLauncherDir()
        {
            try
            {
                var dir = LauncherDataDir;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                OpenFolderInExplorer(dir);
            }
            catch (Exception ex)
            {
                StatusText = $"打开目录失败：{ex.Message}";
            }
        }

        private void ClearLogs()
        {
            try
            {
                int deleted = 0;
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "mc-latest.log"),
                    Path.Combine(AppContext.BaseDirectory, "last_launch_args.txt"),
                    Path.Combine(AppContext.BaseDirectory, "failed_downloads.txt"),
                    Path.Combine(AppContext.BaseDirectory, "crash.log")
                };

                foreach (var f in candidates)
                {
                    try
                    {
                        if (File.Exists(f))
                        {
                            File.Delete(f);
                            deleted++;
                        }
                    }
                    catch { }
                }

                StatusText = $"已清理 {deleted} 个日志文件";
            }
            catch (Exception ex)
            {
                StatusText = $"清理失败：{ex.Message}";
            }
        }

        // ================================================================
        // ★ 缓存相关
        // ================================================================

        private async Task RefreshCacheSizeAsync()
        {
            try
            {
                var size = await Task.Run(() => CacheManager.GetTotalSize());
                CacheSizeText = FormatSize(size);
            }
            catch
            {
                CacheSizeText = "—";
            }
        }

        private async Task ClearCacheAsync()
        {
            if (IsCacheBusy) return;

            var confirm = await _dialogService.ShowConfirmAsync(
                "清理缓存",
                "确定要清理所有缓存吗？\n\n" +
                "将删除：\n" +
                "· Modrinth 项目 / 版本缓存\n" +
                "· Java 扫描结果缓存\n" +
                "· 版本清单缓存\n\n" +
                "清理后首次加载会稍慢，不影响游戏和实例。");
            if (!confirm) return;

            IsCacheBusy = true;
            StatusText = "正在清理缓存...";

            try
            {
                await Task.Run(() => CacheManager.ClearAll());

                // Java 缓存单独存一份，也一并清掉
                await Task.Run(() => JavaInfo.ClearCache());

                await RefreshCacheSizeAsync();
                StatusText = "✅ 缓存已清理";
            }
            catch (Exception ex)
            {
                StatusText = $"清理失败：{ex.Message}";
            }
            finally
            {
                IsCacheBusy = false;
            }
        }

        private void OpenCacheFolder()
        {
            try
            {
                var dir = CacheManager.GetCacheDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                OpenFolderInExplorer(dir);
            }
            catch (Exception ex)
            {
                StatusText = $"打开目录失败：{ex.Message}";
            }
        }

        // ================================================================
        // 工具
        // ================================================================

        private static void OpenFolderInExplorer(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", path);
                else if (OperatingSystem.IsLinux())
                    Process.Start("xdg-open", path);
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

        // ================================================================
        // INotifyPropertyChanged
        // ================================================================

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}