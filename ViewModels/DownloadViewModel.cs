using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodePulse.Models;
using NodePulse.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse.ViewModels;

public enum DownloadTabKind
{
    Instance,
    Mod,
    Modpack,
    DataPack,
    ShaderPack,
    CustomDownload
}

public partial class DownloadTabItem : ObservableObject
{
    public DownloadTabKind Kind { get; set; }
    public string DisplayName { get; set; } = "";

    [ObservableProperty] private bool _isSelected;
}

public partial class DownloadViewModel : ObservableObject
{
    private static DownloadViewModel? _instance;
    public static DownloadViewModel Instance => _instance ??= new DownloadViewModel();

    private DownloadViewModel()
    {
        InitTabs();

        ModVm = new ContentDownloadViewModel("mod", "模组", "mods");
        ModpackVm = new ContentDownloadViewModel("modpack", "整合包", "mods");
        DatapackVm = new ContentDownloadViewModel("datapack", "数据包", "datapacks");
        ShaderVm = new ContentDownloadViewModel("shader", "光影包", "shaderpacks");
        CustomVm = new CustomDownloadViewModel();
    }

    public event Action<string>? NotificationRequested;

    private List<VersionInfo> _allVersions = new();
    private bool _initialLoadDone = false;

    public ObservableCollection<DownloadTabItem> Tabs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstanceTab))]
    [NotifyPropertyChangedFor(nameof(IsModTab))]
    [NotifyPropertyChangedFor(nameof(IsModpackTab))]
    [NotifyPropertyChangedFor(nameof(IsDatapackTab))]
    [NotifyPropertyChangedFor(nameof(IsShaderTab))]
    [NotifyPropertyChangedFor(nameof(IsCustomTab))]
    private DownloadTabKind _selectedTab = DownloadTabKind.Instance;

    public bool IsInstanceTab => SelectedTab == DownloadTabKind.Instance;
    public bool IsModTab => SelectedTab == DownloadTabKind.Mod;
    public bool IsModpackTab => SelectedTab == DownloadTabKind.Modpack;
    public bool IsDatapackTab => SelectedTab == DownloadTabKind.DataPack;
    public bool IsShaderTab => SelectedTab == DownloadTabKind.ShaderPack;
    public bool IsCustomTab => SelectedTab == DownloadTabKind.CustomDownload;

    public ContentDownloadViewModel ModVm { get; }
    public ContentDownloadViewModel ModpackVm { get; }
    public ContentDownloadViewModel DatapackVm { get; }
    public ContentDownloadViewModel ShaderVm { get; }
    public CustomDownloadViewModel CustomVm { get; }

    private void InitTabs()
    {
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.Instance, DisplayName = "实例", IsSelected = true });
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.Mod, DisplayName = "模组" });
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.Modpack, DisplayName = "整合包" });
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.DataPack, DisplayName = "数据包" });
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.ShaderPack, DisplayName = "光影包" });
        Tabs.Add(new DownloadTabItem { Kind = DownloadTabKind.CustomDownload, DisplayName = "自定义下载" });
    }

    [RelayCommand]
    private void SelectTab(DownloadTabItem? tab)
    {
        if (tab == null) return;

        foreach (var t in Tabs)
            t.IsSelected = (t == tab);

        SelectedTab = tab.Kind;
    }

    public void InjectDialogService(IDialogService ds)
    {
        ModVm.DialogService = ds;
        ModpackVm.DialogService = ds;
        DatapackVm.DialogService = ds;
        ShaderVm.DialogService = ds;
        CustomVm.DialogService = ds;
    }

    public void InjectNotificationHandler(Action<string> handler)
    {
        ModVm.NotificationRequested += handler;
        ModpackVm.NotificationRequested += handler;
        DatapackVm.NotificationRequested += handler;
        ShaderVm.NotificationRequested += handler;
        CustomVm.NotificationRequested += handler;
    }

    [ObservableProperty] private ObservableCollection<VersionInfo> _versionList = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedVersionType = "正式版";
    [ObservableProperty] private string _statusMessage = "准备就绪";
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<DownloadJob> Jobs { get; } = new();
    public ObservableCollection<DownloadJob> FilteredJobs { get; } = new();

    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private string _jobSearchText = "";
    [ObservableProperty] private double _overallPercent;
    [ObservableProperty] private int _activeJobCount;

    [ObservableProperty] private VersionConfigViewModel? _config;
    [ObservableProperty] private bool _isConfiguring;

    private string _pendingConfigVersionType = "";
    private string _pendingConfigVersionUrl = "";

    public bool ShowFloatingButton => HasJobs && !IsPanelOpen;

    public List<string> VersionTypeList { get; } = new() { "全部", "正式版", "快照" };

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedVersionTypeChanged(string value) => ApplyFilter();
    partial void OnJobSearchTextChanged(string value) => ApplyJobFilter();

    partial void OnIsPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowFloatingButton));
    }

    public void EnsureLoaded()
    {
        if (_initialLoadDone) return;
        _initialLoadDone = true;
        _ = RefreshList();
    }

    public void AddExternalJob(DownloadJob job)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Insert(0, job);
            RefreshJobCounts();
            UpdateOverall();
        });
    }

    public void NotifyExternalJobUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateOverall();
        });
    }

    public void RemoveExternalJob(DownloadJob job)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Jobs.Remove(job);
            RefreshJobCounts();
            UpdateOverall();
        });
    }

    [RelayCommand]
    private async Task RefreshList()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "正在加载版本列表...";

            var list = await VersionDownloader.GetVersionListAsync();
            _allVersions = list;
            ApplyFilter();

            StatusMessage = $"版本清单加载完成，共 {list.Count} 条";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = _allVersions.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(v => v.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (SelectedVersionType == "正式版")
            query = query.Where(v => v.Type == "release");
        else if (SelectedVersionType == "快照")
            query = query.Where(v => v.Type == "snapshot");

        VersionList = new ObservableCollection<VersionInfo>(query);

        if (_allVersions.Count > 0)
            StatusMessage = $"版本清单加载完成，共 {_allVersions.Count} 条";
    }

    [RelayCommand]
    private void StartConfiguring(VersionInfo? version)
    {
        if (version == null) return;

        _pendingConfigVersionType = version.Type;
        _pendingConfigVersionUrl = version.Url;

        var configVm = new VersionConfigViewModel(version);
        configVm.DownloadRequested += OnConfigDownloadRequested;
        configVm.Cancelled += OnConfigCancelled;

        Config = configVm;
        IsConfiguring = true;
    }

    private void OnConfigCancelled()
    {
        IsConfiguring = false;
        Config = null;
    }

    private async void OnConfigDownloadRequested(ModLoaderConfig config)
    {
        IsConfiguring = false;
        Config = null;

        var targetName = config.FinalVersionName;

        try
        {
            switch (config.SelectedLoader)
            {
                case ModLoaderType.None:
                {
                    var version = new VersionInfo
                    {
                        Id = targetName,
                        Type = _pendingConfigVersionType,
                        Url = _pendingConfigVersionUrl
                    };
                    await RunDownloadPipelineAsync(version, config);
                    return;
                }

                case ModLoaderType.Fabric:
                case ModLoaderType.Quilt:
                {
                    var version = new VersionInfo
                    {
                        Id = targetName,
                        Type = "release",
                        Url = ""
                    };
                    await RunDownloadPipelineAsync(version, config);
                    return;
                }

                case ModLoaderType.Forge:
                {
                    var version = new VersionInfo
                    {
                        Id = targetName,
                        Type = "release",
                        Url = ""
                    };
                    await RunForgePipelineAsync(version, config);
                    return;
                }

                case ModLoaderType.NeoForge:
                {
                    var version = new VersionInfo
                    {
                        Id = targetName,
                        Type = "release",
                        Url = ""
                    };
                    await RunNeoForgePipelineAsync(version, config);
                    return;
                }

                case ModLoaderType.OptiFine:
                {
                    var version = new VersionInfo
                    {
                        Id = targetName,
                        Type = "release",
                        Url = ""
                    };
                    await RunOptiFinePipelineAsync(version, config);
                    return;
                }

                default:
                    StatusMessage = $"暂不支持 {config.SelectedLoader}";
                    return;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"下载失败：{ex.Message}";
        }
    }

    // ================================================================
    // ★ 根据 ModLoaderType 加载图标
    // ================================================================
    private static Avalonia.Media.Imaging.Bitmap? LoadLoaderIcon(ModLoaderType loader)
    {
        var fileName = loader switch
        {
            ModLoaderType.Fabric => "fabric.png",
            ModLoaderType.Forge => "forge.png",
            ModLoaderType.NeoForge => "neoforge.png",
            ModLoaderType.Quilt => "quilt.png",
            ModLoaderType.OptiFine => "optifine.png",
            _ => "vanilla.png"
        };

        return IconLoader.Load(fileName);
    }

    // ================================================================
    // 阶段 → (起点%, 终点%) 映射
    // ================================================================

    private static (double Start, double End) GetStageRange(string? stage)
    {
        if (string.IsNullOrEmpty(stage)) return (-1, -1);

        if (stage.StartsWith("生成 Fabric 配置")) return (0, 3);
        if (stage.StartsWith("生成 Quilt 配置")) return (0, 3);
        if (stage.StartsWith("下载 Java 运行时")) return (3, 5);
        if (stage.StartsWith("下载原版 Json 文件")) return (5, 8);
        if (stage.StartsWith("下载原版支持库文件")) return (8, 25);
        if (stage.StartsWith("下载原版资源文件")) return (25, 95);

        if (stage == "检查游戏环境") return (0, 2);
        if (stage.StartsWith("下载 Forge 安装器")) return (2, 15);
        if (stage.StartsWith("下载 NeoForge 安装器")) return (2, 15);
        if (stage.StartsWith("下载 OptiFine 安装器")) return (2, 15);
        if (stage.StartsWith("下载原版信息")) return (15, 22);

        if (stage.StartsWith("安装 Forge")) return (22, 88);
        if (stage.StartsWith("安装 NeoForge")) return (22, 88);
        if (stage.StartsWith("安装 OptiFine")) return (22, 88);

        if (stage.StartsWith("配置 Forge 实例")) return (88, 94);
        if (stage.StartsWith("配置 NeoForge 实例")) return (88, 94);
        if (stage.StartsWith("配置 OptiFine 实例")) return (88, 94);

        if (stage.StartsWith("安装 Fabric API")) return (88, 94);
        if (stage.StartsWith("校验游戏文件")) return (94, 99);

        if (stage == "已完成") return (100, 100);

        return (-1, -1);
    }

    private void HandleJobProgress(DownloadJob job, DownloadProgress p)
    {
        if (!string.IsNullOrEmpty(p.Stage))
        {
            if (p.Stage != job.LastStageForFiles)
            {
                if (!string.IsNullOrEmpty(job.LastStageForFiles)
                    && job.LastStageTotalForFiles > 0)
                {
                    job.PreviousStagesFilesTotal += job.LastStageTotalForFiles;
                }

                job.LastStageForFiles = p.Stage;
                job.LastStageTotalForFiles = p.Total;
            }
            else if (p.Total > 0)
            {
                job.LastStageTotalForFiles = p.Total;
            }

            job.Stage = p.Stage;
        }

        job.CurrentFile = p.CurrentFile;
        job.StageDescription = p.CurrentFile;

        if (p.ActiveTasks != null && p.ActiveTasks.Count > 0)
            SyncActiveTasks(job, p.ActiveTasks);
        else
            job.ActiveTasks.Clear();

        if (job.Stage == "已完成")
        {
            job.TotalPercent = 100;
        }
        else
        {
            var (stageStart, stageEnd) = GetStageRange(job.Stage);
            if (stageStart >= 0)
            {
                double innerPct = p.Total > 0
                    ? Math.Clamp((double)p.Done / p.Total, 0, 1)
                    : 0;

                double target = stageStart + innerPct * (stageEnd - stageStart);

                if (target > job.TotalPercent)
                    job.TotalPercent = target;
                if (job.TotalPercent < stageStart)
                    job.TotalPercent = stageStart;
            }
        }

        if (p.Total > 0)
        {
            job.FilesDone = p.Done;
            job.FilesTotal = p.Total;
        }

        job.StagePercent = job.TotalPercent;
        job.FilesPercent = job.TotalPercent;
        job.BytesPercent = job.TotalPercent;

        UpdateSpeed(job, p.TotalDownloadedBytes);
        UpdateOverall();
    }

    // ================================================================
    // 原版 / Fabric / Quilt
    // ================================================================

    private async Task RunDownloadPipelineAsync(VersionInfo version, ModLoaderConfig config)
    {
        VersionDownloader.UseMirror = GlobalSettings.Load().UseMirror;

        var now = DateTime.UtcNow;
        var job = new DownloadJob
        {
            VersionId = version.Id,
            VersionType = version.Type,
            State = DownloadJobState.Running,
            Cts = new CancellationTokenSource(),
            Stage = "准备中...",
            StageDescription = "准备中...",
            LastSpeedTime = now,
            LastByteChangeTime = now,
            TargetVersionDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", version.Id),
            IconBitmap = LoadLoaderIcon(config.SelectedLoader)
        };

        Jobs.Insert(0, job);
        RefreshJobCounts();
        UpdateOverall();

        bool success = false;
        bool cancelled = false;
        bool isFabricLike = config.SelectedLoader == ModLoaderType.Fabric
                            || config.SelectedLoader == ModLoaderType.Quilt;
        bool isFabric = config.SelectedLoader == ModLoaderType.Fabric;

        try
        {
            string initStage = isFabricLike
                ? (config.SelectedLoader == ModLoaderType.Quilt
                    ? "生成 Quilt 配置"
                    : "生成 Fabric 配置")
                : "下载原版 Json 文件";

            job.Stage = initStage;
            job.StageDescription = "准备中...";
            job.TotalPercent = 0;

            if (isFabricLike)
            {
                string loaderName = config.SelectedLoader == ModLoaderType.Quilt
                    ? "Quilt" : "Fabric";

                job.Stage = $"生成 {loaderName} 配置";
                job.StageDescription = $"正在生成 {loaderName} 配置...";

                if (config.SelectedLoader == ModLoaderType.Quilt)
                {
                    await QuiltInstaller.GenerateProfileAsync(
                        config.BaseVersion, config.LoaderVersion, version.Id);
                }
                else
                {
                    await FabricInstaller.GenerateProfileAsync(
                        config.BaseVersion, config.LoaderVersion, version.Id);
                }
            }

            await DownloadCoreAsync(version, job, progressStart: 5, progressEnd: 85);

            if (isFabric)
            {
                if (config.InstallFabricApi)
                {
                    job.Stage = "安装 Fabric API";
                    job.StageDescription = "下载 Fabric API...";

                    var apiOk = await FabricInstaller.InstallFabricApiAsync(
                        config.BaseVersion, version.Id, config.FabricApiVersion);

                    if (!apiOk)
                    {
                        job.StageDescription = "Fabric API 下载失败";
                    }
                }
            }

            job.TotalPercent = 100;
            job.StagePercent = 100;
            job.FilesPercent = 100;
            job.BytesPercent = 100;
            job.Stage = "已完成";
            job.StageDescription = "已完成";
            success = true;
            StatusMessage = $"{version.Id} 下载完成";
        }
        catch (TaskCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"下载失败：{ex.Message}";
            job.Stage = "失败";
            job.StageDescription = ex.Message;
        }
        finally
        {
            if (!cancelled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Jobs.Remove(job);
                    UpdateOverall();
                    RefreshJobCounts();
                });
            }

            if (success)
                NotificationRequested?.Invoke($"「{version.Id}」下载完成！");
        }
    }

    // ================================================================
    // Forge
    // ================================================================

    private async Task RunForgePipelineAsync(VersionInfo version, ModLoaderConfig config)
    {
        VersionDownloader.UseMirror = GlobalSettings.Load().UseMirror;

        var now = DateTime.UtcNow;
        var job = new DownloadJob
        {
            VersionId = version.Id,
            VersionType = "Forge",
            State = DownloadJobState.Running,
            Cts = new CancellationTokenSource(),
            Stage = "准备中...",
            StageDescription = "准备中...",
            LastSpeedTime = now,
            LastByteChangeTime = now,
            TargetVersionDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", version.Id),
            IconBitmap = LoadLoaderIcon(ModLoaderType.Forge)
        };

        Jobs.Insert(0, job);
        RefreshJobCounts();
        UpdateOverall();

        bool success = false;
        bool cancelled = false;

        try
        {
            job.Stage = "检查游戏环境";
            job.StageDescription = "准备安装 Forge...";
            job.TotalPercent = 0;

            var forgeProgress = new LatestOnlyProgress<DownloadProgress>(p =>
            {
                HandleJobProgress(job, p);
            });

            await ForgeInstaller.InstallAsync(
                config.BaseVersion, config.LoaderVersion, version.Id, forgeProgress);

            job.TotalPercent = 100;
            job.StagePercent = 100;
            job.FilesPercent = 100;
            job.BytesPercent = 100;
            job.Stage = "已完成";
            job.StageDescription = "已完成";
            job.FilesDone = 100;
            job.FilesTotal = 100;
            job.ActiveTasks.Clear();

            success = true;
            StatusMessage = $"{version.Id} (Forge) 下载完成";
        }
        catch (TaskCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Forge 安装失败：{ex.Message}";
            job.Stage = "失败";
            job.StageDescription = ex.Message;
        }
        finally
        {
            if (!cancelled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Jobs.Remove(job);
                    UpdateOverall();
                    RefreshJobCounts();
                });
            }

            if (success)
                NotificationRequested?.Invoke($"「{version.Id}」Forge 安装完成！");
        }
    }

    // ================================================================
    // NeoForge
    // ================================================================

    private async Task RunNeoForgePipelineAsync(VersionInfo version, ModLoaderConfig config)
    {
        VersionDownloader.UseMirror = GlobalSettings.Load().UseMirror;

        var now = DateTime.UtcNow;
        var job = new DownloadJob
        {
            VersionId = version.Id,
            VersionType = "NeoForge",
            State = DownloadJobState.Running,
            Cts = new CancellationTokenSource(),
            Stage = "准备中...",
            StageDescription = "准备中...",
            LastSpeedTime = now,
            LastByteChangeTime = now,
            TargetVersionDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", version.Id),
            IconBitmap = LoadLoaderIcon(ModLoaderType.NeoForge)
        };

        Jobs.Insert(0, job);
        RefreshJobCounts();
        UpdateOverall();

        bool success = false;
        bool cancelled = false;

        try
        {
            job.Stage = "检查游戏环境";
            job.StageDescription = "准备安装 NeoForge...";
            job.TotalPercent = 0;

            var progress = new LatestOnlyProgress<DownloadProgress>(p =>
            {
                HandleJobProgress(job, p);
            });

            await NeoForgeInstaller.InstallAsync(
                config.BaseVersion, config.LoaderVersion, version.Id, progress);

            job.TotalPercent = 100;
            job.StagePercent = 100;
            job.FilesPercent = 100;
            job.BytesPercent = 100;
            job.Stage = "已完成";
            job.StageDescription = "已完成";
            job.FilesDone = 100;
            job.FilesTotal = 100;
            job.ActiveTasks.Clear();

            success = true;
            StatusMessage = $"{version.Id} (NeoForge) 下载完成";
        }
        catch (TaskCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"NeoForge 安装失败：{ex.Message}";
            job.Stage = "失败";
            job.StageDescription = ex.Message;
        }
        finally
        {
            if (!cancelled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Jobs.Remove(job);
                    UpdateOverall();
                    RefreshJobCounts();
                });
            }

            if (success)
                NotificationRequested?.Invoke($"「{version.Id}」NeoForge 安装完成！");
        }
    }

    // ================================================================
    // OptiFine
    // ================================================================

    private async Task RunOptiFinePipelineAsync(VersionInfo version, ModLoaderConfig config)
    {
        VersionDownloader.UseMirror = GlobalSettings.Load().UseMirror;

        var now = DateTime.UtcNow;
        var job = new DownloadJob
        {
            VersionId = version.Id,
            VersionType = "OptiFine",
            State = DownloadJobState.Running,
            Cts = new CancellationTokenSource(),
            Stage = "准备中...",
            StageDescription = "准备中...",
            LastSpeedTime = now,
            LastByteChangeTime = now,
            TargetVersionDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", version.Id),
            IconBitmap = LoadLoaderIcon(ModLoaderType.OptiFine)
        };

        Jobs.Insert(0, job);
        RefreshJobCounts();
        UpdateOverall();

        bool success = false;
        bool cancelled = false;

        try
        {
            job.Stage = "检查游戏环境";
            job.StageDescription = "准备安装 OptiFine...";
            job.TotalPercent = 0;

            var progress = new LatestOnlyProgress<DownloadProgress>(p =>
            {
                HandleJobProgress(job, p);
            });

            await OptiFineInstaller.InstallAsync(
                config.BaseVersion, config.LoaderVersion, version.Id, progress);

            job.TotalPercent = 100;
            job.StagePercent = 100;
            job.FilesPercent = 100;
            job.BytesPercent = 100;
            job.Stage = "已完成";
            job.StageDescription = "已完成";
            job.FilesDone = 100;
            job.FilesTotal = 100;
            job.ActiveTasks.Clear();

            success = true;
            StatusMessage = $"{version.Id} (OptiFine) 下载完成";
        }
        catch (TaskCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"OptiFine 安装失败：{ex.Message}";
            job.Stage = "失败";
            job.StageDescription = ex.Message;
        }
        finally
        {
            if (!cancelled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Jobs.Remove(job);
                    UpdateOverall();
                    RefreshJobCounts();
                });
            }

            if (success)
                NotificationRequested?.Invoke($"「{version.Id}」OptiFine 安装完成！");
        }
    }

    private async Task DownloadCoreAsync(
        VersionInfo version, DownloadJob job,
        double progressStart, double progressEnd)
    {
        var progress = new LatestOnlyProgress<DownloadProgress>(p =>
        {
            HandleJobProgress(job, p);
        });

        await VersionDownloader.DownloadEverythingAsync(version, progress);
    }

    private static void UpdateSpeed(DownloadJob job, long currentBytes)
    {
        if (currentBytes <= 0) return;

        var now = DateTime.UtcNow;
        var dt = (now - job.LastSpeedTime).TotalSeconds;
        if (dt < 0.5) return;

        var dBytes = currentBytes - job.LastSpeedBytes;

        if (dBytes > 0)
        {
            double instant = dBytes / dt;
            if (job.SmoothedSpeed <= 0) job.SmoothedSpeed = instant;
            else job.SmoothedSpeed = job.SmoothedSpeed * 0.6 + instant * 0.4;

            job.SpeedText = FormatSpeed(job.SmoothedSpeed);
            job.LastByteChangeTime = now;
        }
        else
        {
            double idleSec = (now - job.LastByteChangeTime).TotalSeconds;
            if (job.SmoothedSpeed <= 0)
                job.SpeedText = idleSec > 3 ? "连接中..." : "";
            else if (idleSec > 15)
            {
                job.SpeedText = "停滞";
                job.SmoothedSpeed = 0;
            }
        }

        job.LastSpeedBytes = currentBytes;
        job.LastSpeedTime = now;
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1) return "0 B/s";
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec / 1024 / 1024:F2} MB/s";
    }

    private static void SyncActiveTasks(DownloadJob job, List<DownloadTaskProgress> snapshot)
    {
        var keys = new HashSet<string>(snapshot.Select(s => s.Key));
        for (int i = job.ActiveTasks.Count - 1; i >= 0; i--)
            if (!keys.Contains(job.ActiveTasks[i].Key))
                job.ActiveTasks.RemoveAt(i);

        foreach (var s in snapshot)
        {
            var existing = job.ActiveTasks.FirstOrDefault(t => t.Key == s.Key);
            if (existing == null)
            {
                job.ActiveTasks.Add(new ActiveDownloadTask
                {
                    Key = s.Key,
                    FileName = s.Name,
                    Url = s.Url,
                    DownloadedBytes = s.DownloadedBytes,
                    TotalBytes = s.TotalBytes,
                    Percent = s.Percent
                });
            }
            else
            {
                existing.DownloadedBytes = s.DownloadedBytes;
                existing.TotalBytes = s.TotalBytes;
                existing.Percent = s.Percent;
            }
        }
    }

    [RelayCommand] private void TogglePanel() => IsPanelOpen = !IsPanelOpen;
    [RelayCommand] private void ClosePanel() => IsPanelOpen = false;
    [RelayCommand] private void OpenPanel() => IsPanelOpen = true;

    [RelayCommand]
    private void Refresh()
    {
        ApplyJobFilter();
        UpdateOverall();
        RefreshJobCounts();
    }

    [RelayCommand]
    private void ToggleExpand(DownloadJob? job)
    {
        if (job != null) job.IsExpanded = !job.IsExpanded;
    }

    [RelayCommand]
    private async Task CancelJob(DownloadJob? job)
    {
        if (job == null) return;

        try { job.Cts?.Cancel(); } catch { }

        job.Stage = "正在取消...";
        job.StageDescription = "正在取消...";

        await Task.Delay(1500);

        if (!string.IsNullOrEmpty(job.TargetVersionDir))
        {
            try
            {
                if (Directory.Exists(job.TargetVersionDir))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            Directory.Delete(job.TargetVersionDir, recursive: true);
                            break;
                        }
                        catch (IOException)
                        {
                            await Task.Delay(500);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DownloadVM] 删除下载目录失败：{ex.Message}");
            }
        }

        Jobs.Remove(job);
        RefreshJobCounts();
        UpdateOverall();

        StatusMessage = $"{job.VersionId} 已取消";
    }

    private void RefreshJobCounts()
    {
        ActiveJobCount = Jobs.Count(j =>
            j.State == DownloadJobState.Running ||
            j.State == DownloadJobState.Pending);

        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(HasActiveJobs));
        OnPropertyChanged(nameof(ShowFloatingButton));
        ApplyJobFilter();

        if (Jobs.Count == 0 && IsPanelOpen)
            IsPanelOpen = false;
    }

    public bool HasJobs => Jobs.Count > 0;
    public bool HasActiveJobs => ActiveJobCount > 0;

    private void ApplyJobFilter()
    {
        FilteredJobs.Clear();
        var query = Jobs.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(JobSearchText))
        {
            query = query.Where(j =>
                j.VersionId.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase) ||
                j.Title.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var j in query)
            FilteredJobs.Add(j);
    }

    private void UpdateOverall()
    {
        var active = Jobs
            .Where(j => j.State == DownloadJobState.Running ||
                        j.State == DownloadJobState.Pending)
            .ToList();

        if (active.Count > 0)
            OverallPercent = active.Average(j => j.TotalPercent);
        else if (Jobs.Count > 0)
            OverallPercent = Jobs.Average(j => j.TotalPercent);
        else
            OverallPercent = 0;

        OnPropertyChanged(nameof(HasActiveJobs));
        OnPropertyChanged(nameof(ShowFloatingButton));
    }
}

internal sealed class LatestOnlyProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly object _lock = new();
    private T? _pending;
    private bool _scheduled;

    public LatestOnlyProgress(Action<T> handler) { _handler = handler; }

    public void Report(T value)
    {
        lock (_lock)
        {
            _pending = value;
            if (_scheduled) return;
            _scheduled = true;
        }
        Dispatcher.UIThread.Post(Drain);
    }

    private void Drain()
    {
        T? value;
        lock (_lock)
        {
            value = _pending;
            _pending = default;
            _scheduled = false;
        }
        if (value != null) _handler(value);
    }
}