using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodePulse.Models;
using NodePulse.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NodePulse.ViewModels;

public partial class ContentDownloadViewModel : ObservableObject
{
    public string ProjectType { get; }
    public string DisplayName { get; }
    public string DefaultSubFolder { get; }

    /// <summary>每页条数</summary>
    private const int PageSize = 20;

    public event Action? ResultsUpdated;

    private static List<MinecraftInstance>? _cachedInstances;
    private static readonly object _instancesLock = new();

    // ============ 分页状态 ============

    private string _lastQuery = "";
    private string? _lastGameVersion;
    private string? _lastLoader;
    private bool _lastSortByDownloads;
    private bool _isPopularMode;

    public ContentDownloadViewModel(string projectType, string displayName, string defaultSubFolder)
    {
        ProjectType = projectType;
        DisplayName = displayName;
        DefaultSubFolder = defaultSubFolder;
    }

    public IDialogService? DialogService { get; set; }
    public event Action<string>? NotificationRequested;

    // ============ 搜索 ============

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private bool _isSearching;

    public ObservableCollection<string> LoaderOptions { get; } = new()
    {
        "全部", "Fabric", "Forge", "NeoForge", "Quilt"
    };

    public ObservableCollection<string> GameVersionOptions { get; } = new()
    {
        "全部", "1.21.4", "1.21.3", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1",
        "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.12.2", "1.7.10"
    };

    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "相关度", "下载量", "关注数"
    };

    [ObservableProperty] private string _selectedLoader = "全部";
    [ObservableProperty] private string _selectedGameVersion = "全部";
    [ObservableProperty] private string _selectedSort = "相关度";

    /// <summary>当前页的结果列表（每页固定 20 条）</summary>
    public ObservableCollection<ModrinthSearchHit> SearchResults { get; } = new();

    [ObservableProperty] private string _listTitle = "热门";
    [ObservableProperty] private string _statusMessage = "";

    // ============ 分页属性 ============

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfoText))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfoText))]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _totalPages = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageInfoText))]
    private int _totalCount = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrev))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private bool _isPageLoading;

    [ObservableProperty] private bool _isFirstLoading;

    public bool CanGoPrev => CurrentPage > 1 && !IsPageLoading;
    public bool CanGoNext => CurrentPage < TotalPages && !IsPageLoading;

    public string PageInfoText =>
        TotalPages > 0
            ? $"第 {CurrentPage} / {TotalPages} 页  ·  共 {TotalCount} 项"
            : "";

    // ============ 详情 ============

    [ObservableProperty] private bool _isDetailView;
    [ObservableProperty] private bool _isLoadingDetail;
    [ObservableProperty] private ModrinthProject? _currentProject;

    public ObservableCollection<ModrinthVersion> ReleaseVersions { get; } = new();
    public ObservableCollection<ModrinthVersion> BetaVersions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BetaHeaderArrow))]
    private bool _isBetaExpanded;

    public bool HasBetaVersions => BetaVersions.Count > 0;
    public string BetaHeaderText => $"测试版（{BetaVersions.Count}）";
    public string BetaHeaderArrow => IsBetaExpanded ? "▾" : "▸";

    private string _preferredGameVersion = "";
    private string _preferredLoader = "";

    public ObservableCollection<MinecraftInstance> Instances { get; } = new();
    [ObservableProperty] private MinecraftInstance? _selectedInstance;

    public ObservableCollection<ModDownloadJob> DownloadJobs { get; } = new();

    // ================================================================
    // 实例列表
    // ================================================================

    public async Task LoadInstancesAsync()
    {
        try
        {
            var list = _cachedInstances;
            if (list == null)
            {
                list = await Task.Run(() => MinecraftInstance.ScanAll());
                lock (_instancesLock)
                {
                    _cachedInstances ??= list;
                    list = _cachedInstances;
                }
            }

            Instances.Clear();
            foreach (var inst in list)
                Instances.Add(inst);

            if (Instances.Count > 0 && SelectedInstance == null)
                SelectedInstance = Instances[0];
        }
        catch { }
    }

    public static void InvalidateInstanceCache()
    {
        lock (_instancesLock) { _cachedInstances = null; }
    }

    // ================================================================
    // 内存 / 释放
    // ================================================================

    /// <summary>释放这个 Tab 的所有图标 Bitmap（切走 Tab 时调用）</summary>
    public void ReleaseIcons()
    {
        foreach (var hit in SearchResults)
        {
            try { hit.IconBitmap = null; } catch { }
        }

        try
        {
            if (CurrentProject != null)
                CurrentProject.IconBitmap = null;
        }
        catch { }
    }

    /// <summary>完全清空</summary>
    public void ReleaseAll()
    {
        ReleaseIcons();
        SearchResults.Clear();
        ReleaseVersions.Clear();
        BetaVersions.Clear();
        DownloadJobs.Clear();
        CurrentProject = null;
        CurrentPage = 1;
        TotalPages = 0;
        TotalCount = 0;
    }

    /// <summary>清空当前页的结果 + 释放 bitmap（翻页前调用）</summary>
    private void ClearCurrentResults()
    {
        foreach (var hit in SearchResults)
        {
            try { hit.IconBitmap = null; } catch { }
        }
        SearchResults.Clear();
    }

    // ================================================================
    // 分页核心
    // ================================================================

    /// <summary>
    /// 加载当前页（CurrentPage）的数据，替换 SearchResults。
    /// </summary>
    private async Task LoadPageAsync()
    {
        if (IsPageLoading) return;

        IsPageLoading = true;

        // 清空旧页（卸载渲染 + 释放 bitmap）
        ClearCurrentResults();

        try
        {
            int offset = (CurrentPage - 1) * PageSize;

            var resp = await ModrinthService.SearchModsAsync(
                _isPopularMode ? "" : _lastQuery,
                _lastGameVersion,
                _lastLoader,
                offset: offset,
                limit: PageSize,
                sortByDownloads: _lastSortByDownloads,
                projectType: ProjectType);

            if (resp?.Hits != null)
            {
                foreach (var hit in resp.Hits)
                    SearchResults.Add(hit);

                TotalCount = resp.TotalHits;
                TotalPages = TotalCount > 0
                    ? (int)Math.Ceiling(TotalCount / (double)PageSize)
                    : 0;

                // 越界保护：比如总共只有 3 页但用户点到了第 5 页
                if (SearchResults.Count == 0 && CurrentPage > 1 && TotalPages > 0)
                {
                    CurrentPage = TotalPages;
                    IsPageLoading = false;
                    await LoadPageAsync();
                    return;
                }

                // 加载当前页的图标
                if (resp.Hits.Count > 0)
                    _ = LoadIconsAsync(resp.Hits);

                ResultsUpdated?.Invoke();
            }
            else
            {
                StatusMessage = "加载失败，请检查网络";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsPageLoading = false;
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext) return;
        CurrentPage++;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanGoPrev) return;
        CurrentPage--;
        await LoadPageAsync();
    }

    // ================================================================
    // 搜索
    // ================================================================

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            StatusMessage = "请输入搜索关键词";
            return;
        }

        IsSearching = true;
        StatusMessage = "正在搜索...";
        ListTitle = $"搜索：{SearchQuery}";

        _isPopularMode = false;
        _lastQuery = SearchQuery.Trim();
        _lastGameVersion = SelectedGameVersion == "全部" ? null : SelectedGameVersion;
        _lastLoader = SelectedLoader == "全部" ? null : SelectedLoader.ToLowerInvariant();
        _lastSortByDownloads = SelectedSort == "下载量";

        CurrentPage = 1;
        TotalPages = 0;
        TotalCount = 0;

        await LoadPageAsync();

        StatusMessage = TotalCount > 0 ? $"找到 {TotalCount} 项" : "没有找到结果";
        IsSearching = false;
    }

    // ================================================================
    // 热门
    // ================================================================

    public async Task LoadPopularAsync()
    {
        if (IsFirstLoading) return;

        IsFirstLoading = true;
        IsSearching = true;
        StatusMessage = $"正在加载热门{DisplayName}...";
        ListTitle = $"热门{DisplayName}";

        _isPopularMode = true;
        _lastQuery = "";
        _lastGameVersion = null;
        _lastLoader = null;
        _lastSortByDownloads = true;

        CurrentPage = 1;
        TotalPages = 0;
        TotalCount = 0;

        try
        {
            await LoadPageAsync();
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
            IsFirstLoading = false;
        }
    }

    // ================================================================
    // 图标异步加载
    // ================================================================

    private async Task LoadIconsAsync(IEnumerable<ModrinthSearchHit> hits)
    {
        using var sem = new System.Threading.SemaphoreSlim(2);
        var tasks = new List<Task>();

        foreach (var hit in hits.ToList())
        {
            var local = hit;
            if (local.IconBitmap != null) continue;

            tasks.Add(Task.Run(async () =>
            {
                await sem.WaitAsync();
                try
                {
                    var bmp = await ModrinthService.LoadIconAsync(local.IconUrl);
                    if (bmp != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            // 二次校验：如果翻页了，这个 hit 已经不在列表里，丢弃
                            if (SearchResults.Contains(local))
                                local.IconBitmap = bmp;
                        });
                    }
                }
                finally
                {
                    sem.Release();
                }
            }));
        }

        try { await Task.WhenAll(tasks); }
        catch { }
    }

    // ================================================================
    // 详情
    // ================================================================

    [RelayCommand]
    private async Task OpenDetailAsync(ModrinthSearchHit? hit)
    {
        if (hit == null) return;

        IsDetailView = true;
        IsLoadingDetail = true;
        IsBetaExpanded = false;

        try
        {
            if (CurrentProject != null)
                CurrentProject.IconBitmap = null;
        }
        catch { }

        CurrentProject = null;
        ReleaseVersions.Clear();
        BetaVersions.Clear();

        _preferredGameVersion = SelectedGameVersion == "全部" ? "" : SelectedGameVersion;
        _preferredLoader = SelectedLoader == "全部" ? "" : SelectedLoader.ToLowerInvariant();

        try
        {
            var projectTask = ModrinthService.GetProjectAsync(hit.ProjectId);
            var versionsTask = ModrinthService.GetVersionsAsync(hit.ProjectId);

            await Task.WhenAll(projectTask, versionsTask);

            CurrentProject = projectTask.Result;
            if (CurrentProject != null)
                _ = LoadProjectIconAsync(CurrentProject);

            var all = versionsTask.Result;
            var release = all.Where(v => v.VersionType == "release").ToList();
            var beta = all.Where(v => v.VersionType != "release").ToList();

            release = SortByPreference(release);
            beta = SortByPreference(beta);

            foreach (var v in release)
                ReleaseVersions.Add(v);
            foreach (var v in beta)
                BetaVersions.Add(v);

            OnPropertyChanged(nameof(HasBetaVersions));
            OnPropertyChanged(nameof(BetaHeaderText));

            if (CurrentProject == null)
                StatusMessage = "加载项目详情失败";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开详情失败：{ex.Message}";
        }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    private List<ModrinthVersion> SortByPreference(List<ModrinthVersion> versions)
    {
        return versions
            .OrderByDescending(v => MatchScore(v))
            .ThenByDescending(v => ParseDate(v.DatePublished))
            .ToList();
    }

    private int MatchScore(ModrinthVersion v)
    {
        int score = 0;

        if (!string.IsNullOrEmpty(_preferredGameVersion) &&
            v.GameVersions.Any(g => g.Equals(_preferredGameVersion,
                StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        if (!string.IsNullOrEmpty(_preferredLoader) &&
            v.Loaders.Any(l => l.Equals(_preferredLoader,
                StringComparison.OrdinalIgnoreCase)))
        {
            score += 1;
        }

        return score;
    }

    private static DateTime ParseDate(string s)
    {
        return DateTime.TryParse(s, out var dt) ? dt : DateTime.MinValue;
    }

    private async Task LoadProjectIconAsync(ModrinthProject project)
    {
        var bmp = await ModrinthService.LoadIconAsync(project.IconUrl);
        if (bmp != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(CurrentProject, project))
                    project.IconBitmap = bmp;
            });
        }
    }

    [RelayCommand]
    private void CloseDetail()
    {
        IsDetailView = false;

        try
        {
            if (CurrentProject != null)
                CurrentProject.IconBitmap = null;
        }
        catch { }

        CurrentProject = null;

        ReleaseVersions.Clear();
        BetaVersions.Clear();
        IsBetaExpanded = false;
        StatusMessage = "";

        ResultsUpdated?.Invoke();
    }

    [RelayCommand]
    private void ToggleBeta()
    {
        IsBetaExpanded = !IsBetaExpanded;
    }

    // ================================================================
    // 安装
    // ================================================================

    [RelayCommand]
    private async Task InstallVersionAsync(ModrinthVersion? version)
    {
        if (version == null) return;

        var file = version.PrimaryFile;
        if (file == null)
        {
            StatusMessage = "该版本没有可下载的文件";
            return;
        }

        var name = CurrentProject?.Title ?? "未知项目";
        await ShowSaveAndDownloadAsync(file, name);
    }

    [RelayCommand]
    private async Task InstallModAsync(ModrinthSearchHit? hit)
    {
        if (hit == null) return;

        StatusMessage = $"正在获取「{hit.Title}」的版本...";

        try
        {
            var versions = await ModrinthService.GetVersionsAsync(
                hit.ProjectId,
                gameVersion: null,
                loader: null);

            if (versions.Count == 0)
            {
                StatusMessage = $"「{hit.Title}」没有可用版本";
                return;
            }

            var preferredGame = SelectedGameVersion == "全部" ? "" : SelectedGameVersion;
            var preferredLoader = SelectedLoader == "全部" ? "" : SelectedLoader.ToLowerInvariant();

            var sorted = versions
                .OrderByDescending(v =>
                {
                    int score = 0;
                    if (!string.IsNullOrEmpty(preferredGame) &&
                        v.GameVersions.Any(g => g.Equals(preferredGame, StringComparison.OrdinalIgnoreCase)))
                        score += 2;
                    if (!string.IsNullOrEmpty(preferredLoader) &&
                        v.Loaders.Any(l => l.Equals(preferredLoader, StringComparison.OrdinalIgnoreCase)))
                        score += 1;
                    return score;
                })
                .ThenByDescending(v => ParseDate(v.DatePublished))
                .ToList();

            var file = sorted[0].PrimaryFile;
            if (file == null)
            {
                StatusMessage = "该版本没有可下载的文件";
                return;
            }

            await ShowSaveAndDownloadAsync(file, hit.Title);
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装失败：{ex.Message}";
        }
    }

    private async Task ShowSaveAndDownloadAsync(ModrinthFile file, string itemName)
    {
        if (DialogService == null)
        {
            StatusMessage = "对话框服务未初始化";
            return;
        }

        string? startDir = null;
        if (SelectedInstance != null)
        {
            var dir = Path.Combine(
                VersionScanner.MinecraftFolder,
                "versions",
                SelectedInstance.Name,
                DefaultSubFolder);

            try
            {
                Directory.CreateDirectory(dir);
                startDir = dir;
            }
            catch { }
        }

        var targetPath = await DialogService.ShowSaveFileAsync(
            title: $"保存{DisplayName} - {itemName}",
            suggestedFileName: file.Filename,
            defaultExtension: "jar",
            startDirectory: startDir);

        if (string.IsNullOrEmpty(targetPath))
        {
            StatusMessage = "已取消";
            return;
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(targetDir))
        {
            StatusMessage = "目标路径无效";
            return;
        }

        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建目录失败：{ex.Message}";
            return;
        }

        var globalVm = DownloadViewModel.Instance;

        var globalJob = new DownloadJob
        {
            VersionId = itemName,
            VersionType = DisplayName,
            VersionName = itemName,
            State = DownloadJobState.Running,
            Cts = new System.Threading.CancellationTokenSource(),
            Stage = "准备下载...",
            StageDescription = "准备下载...",
            LastSpeedTime = DateTime.UtcNow,
            LastByteChangeTime = DateTime.UtcNow
        };

        globalVm.AddExternalJob(globalJob);

        var localJob = new ModDownloadJob
        {
            ModName = itemName,
            FileName = Path.GetFileName(targetPath),
            TargetPath = targetPath,
            State = "下载中..."
        };
        DownloadJobs.Insert(0, localJob);

        StatusMessage = $"正在下载 {Path.GetFileName(targetPath)}...";

        long prevBytes = 0;
        var lastTime = DateTime.UtcNow;

        try
        {
            await Task.Run(async () =>
            {
                await ModrinthService.DownloadFileToPathAsync(
                    file,
                    targetPath,
                    (read, total) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            double pct = total > 0 ? (double)read / total * 100 : 0;

                            localJob.DownloadedBytes = read;
                            localJob.TotalBytes = total;
                            localJob.Percent = pct;

                            globalJob.TotalPercent = pct;
                            globalJob.StagePercent = pct;
                            globalJob.BytesPercent = pct;
                            globalJob.CurrentFile = Path.GetFileName(targetPath);
                            globalJob.Stage = "下载中...";
                            globalJob.StageDescription = total > 0
                                ? $"{read / 1024.0 / 1024:F1} / {total / 1024.0 / 1024:F1} MB"
                                : $"{read / 1024.0 / 1024:F1} MB";
                            globalJob.DownloadedSize = $"{read / 1024.0 / 1024:F1} MB";
                            globalJob.TotalSize = total > 0
                                ? $"{total / 1024.0 / 1024:F1} MB"
                                : "";

                            var now = DateTime.UtcNow;
                            var dt = (now - lastTime).TotalSeconds;
                            if (dt >= 0.5)
                            {
                                long delta = read - prevBytes;
                                if (delta > 0)
                                {
                                    double speed = delta / dt;
                                    globalJob.SpeedText = speed < 1024
                                        ? $"{speed:F0} B/s"
                                        : speed < 1024 * 1024
                                            ? $"{speed / 1024:F1} KB/s"
                                            : $"{speed / 1024 / 1024:F2} MB/s";
                                }
                                prevBytes = read;
                                lastTime = now;
                            }

                            globalVm.NotifyExternalJobUpdated();

                            StatusMessage = $"正在下载 {Path.GetFileName(targetPath)}... {pct:F0}%";
                        });
                    });
            });

            globalJob.TotalPercent = 100;
            globalJob.StagePercent = 100;
            globalJob.BytesPercent = 100;
            globalJob.State = DownloadJobState.Completed;
            globalJob.Stage = "已完成";
            globalJob.StageDescription = "已完成";
            globalVm.NotifyExternalJobUpdated();

            localJob.State = "已完成";
            localJob.Percent = 100;

            StatusMessage = $"「{Path.GetFileName(targetPath)}」下载完成";
            NotificationRequested?.Invoke($"{DisplayName}「{itemName}」下载完成");

            _ = Task.Delay(2000).ContinueWith(_ =>
            {
                globalVm.RemoveExternalJob(globalJob);
            });
        }
        catch (Exception ex)
        {
            globalJob.State = DownloadJobState.Failed;
            globalJob.Stage = "失败";
            globalJob.StageDescription = ex.Message;
            globalJob.BytesPercent = 0;
            globalVm.NotifyExternalJobUpdated();

            localJob.State = $"失败：{ex.Message}";
            StatusMessage = $"下载失败：{ex.Message}";
            NotificationRequested?.Invoke($"下载失败：{ex.Message}");

            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                globalVm.RemoveExternalJob(globalJob);
            });
        }
    }
}