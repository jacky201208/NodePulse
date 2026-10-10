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

    // ============ 数据源 ============

    public ObservableCollection<string> DataSourceOptions { get; } = new()
    {
        "全部", "Modrinth", "CurseForge"
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurseForge))]
    [NotifyPropertyChangedFor(nameof(IsModrinth))]
    [NotifyPropertyChangedFor(nameof(IsMerged))]
    [NotifyPropertyChangedFor(nameof(SourceBadgeVisibility))]
    private string _selectedDataSource = "全部";

    public bool IsCurseForge => SelectedDataSource == "CurseForge";
    public bool IsModrinth => SelectedDataSource == "Modrinth";
    public bool IsMerged => SelectedDataSource == "全部";
    public bool SourceBadgeVisibility => !IsMerged;

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

    private async Task LoadPageAsync()
    {
        if (IsPageLoading) return;

        IsPageLoading = true;
        ClearCurrentResults();

        try
        {
            int offset = (CurrentPage - 1) * PageSize;

            if (IsMerged)
            {
                await LoadMergedPageAsync(offset);
            }
            else if (IsCurseForge)
            {
                await LoadCurseForgePageAsync(offset);
            }
            else
            {
                await LoadModrinthPageAsync(offset);
            }

            ResultsUpdated?.Invoke();
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

    private async Task LoadModrinthPageAsync(int offset)
    {
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

            if (SearchResults.Count == 0 && CurrentPage > 1 && TotalPages > 0)
            {
                CurrentPage = TotalPages;
                IsPageLoading = false;
                await LoadPageAsync();
                return;
            }

            if (resp.Hits.Count > 0)
                _ = LoadIconsAsync(resp.Hits, isCf: false);
        }
        else
        {
            StatusMessage = "加载失败，请检查网络";
        }
    }

    private async Task LoadCurseForgePageAsync(int offset)
    {
        var classId = CurseForgeService.GetClassId(ProjectType);
        var resp = await CurseForgeService.SearchModsAsync(
            _isPopularMode ? "" : _lastQuery,
            _lastGameVersion,
            _lastLoader,
            offset: offset,
            limit: PageSize,
            sortByDownloads: _lastSortByDownloads,
            classId: classId);

        if (resp?.Data != null)
        {
            foreach (var hit in resp.Data)
                SearchResults.Add(CurseForgeConverter.ToSearchHit(hit));

            TotalCount = resp.Pagination?.TotalCount ?? resp.Data.Count;
            TotalPages = TotalCount > 0
                ? (int)Math.Ceiling(TotalCount / (double)PageSize)
                : 0;

            if (SearchResults.Count == 0 && CurrentPage > 1 && TotalPages > 0)
            {
                CurrentPage = TotalPages;
                IsPageLoading = false;
                await LoadPageAsync();
                return;
            }

            if (resp.Data.Count > 0)
                _ = LoadIconsAsync(resp.Data.Select(h => CurseForgeConverter.ToSearchHit(h)), isCf: true);
        }
    }

    private async Task LoadMergedPageAsync(int offset)
    {
        var cfClassId = CurseForgeService.GetClassId(ProjectType);

        var mrTask = ModrinthService.SearchModsAsync(
            _isPopularMode ? "" : _lastQuery,
            _lastGameVersion,
            _lastLoader,
            offset: offset,
            limit: PageSize,
            sortByDownloads: _lastSortByDownloads,
            projectType: ProjectType);

        var cfTask = CurseForgeService.SearchModsAsync(
            _isPopularMode ? "" : _lastQuery,
            _lastGameVersion,
            _lastLoader,
            offset: offset,
            limit: PageSize,
            sortByDownloads: _lastSortByDownloads,
            classId: cfClassId);

        await Task.WhenAll(mrTask, cfTask);

        var allHits = new List<ModrinthSearchHit>();

        var cfResp = cfTask.Result;
        if (cfResp?.Data != null)
        {
            foreach (var hit in cfResp.Data)
                allHits.Add(CurseForgeConverter.ToSearchHit(hit));
        }

        var mrResp = mrTask.Result;
        if (mrResp?.Hits != null)
        {
            foreach (var hit in mrResp.Hits)
                allHits.Add(hit);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ModrinthSearchHit>();
        foreach (var hit in allHits)
        {
            if (seen.Add(hit.Title))
                deduped.Add(hit);
        }

        var page = deduped.Take(PageSize).ToList();
        foreach (var h in page)
            SearchResults.Add(h);

        int mrTotal = mrResp?.TotalHits ?? 0;
        int cfTotal = cfResp?.Pagination?.TotalCount ?? 0;
        TotalCount = Math.Min(mrTotal + cfTotal, Math.Max(mrTotal, cfTotal) * 2);
        TotalPages = TotalCount > 0
            ? (int)Math.Ceiling(TotalCount / (double)PageSize)
            : 0;
        if (TotalPages > 50) TotalPages = 50;

        if (SearchResults.Count == 0 && CurrentPage > 1 && TotalPages > 0)
        {
            CurrentPage = TotalPages;
            IsPageLoading = false;
            await LoadPageAsync();
            return;
        }

        if (page.Count > 0)
        {
            var cfHits = page.Where(h => IsCfHit(h)).ToList();
            var mrHits = page.Where(h => !IsCfHit(h)).ToList();
            if (cfHits.Count > 0) _ = LoadIconsAsync(cfHits, isCf: true);
            if (mrHits.Count > 0) _ = LoadIconsAsync(mrHits, isCf: false);
        }
    }

    private static bool IsCfHit(ModrinthSearchHit hit) =>
        long.TryParse(hit.ProjectId, out _);

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

    private async Task LoadIconsAsync(IEnumerable<ModrinthSearchHit> hits, bool isCf)
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
                    var bmp = isCf
                        ? await CurseForgeService.LoadIconAsync(local.IconUrl)
                        : await ModrinthService.LoadIconAsync(local.IconUrl);
                    if (bmp != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
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

        try { if (CurrentProject != null) CurrentProject.IconBitmap = null; } catch { }

        CurrentProject = null;
        ReleaseVersions.Clear();
        BetaVersions.Clear();

        _preferredGameVersion = SelectedGameVersion == "全部" ? "" : SelectedGameVersion;
        _preferredLoader = SelectedLoader == "全部" ? "" : SelectedLoader.ToLowerInvariant();

        try
        {
            var isCf = IsMerged ? IsCfHit(hit) : IsCurseForge;

            if (isCf)
            {
                if (!long.TryParse(hit.ProjectId, out var cfId)) return;
                var detail = await CurseForgeService.GetModDetailAsync(cfId);
                var files = await CurseForgeService.GetFilesAsync(cfId);

                if (detail != null)
                {
                    CurrentProject = CurseForgeConverter.ToProject(detail);
                    _ = LoadProjectIconAsync(CurrentProject, isCf: true);
                }

                var all = files.Select(CurseForgeConverter.ToVersion).ToList();
                var release = all.Where(v => v.VersionType == "release").ToList();
                var beta = all.Where(v => v.VersionType != "release").ToList();

                release = SortByPreference(release);
                beta = SortByPreference(beta);

                foreach (var v in release) ReleaseVersions.Add(v);
                foreach (var v in beta) BetaVersions.Add(v);
            }
            else
            {
                var projectTask = ModrinthService.GetProjectAsync(hit.ProjectId);
                var versionsTask = ModrinthService.GetVersionsAsync(hit.ProjectId);

                await Task.WhenAll(projectTask, versionsTask);

                CurrentProject = projectTask.Result;
                if (CurrentProject != null)
                    _ = LoadProjectIconAsync(CurrentProject, isCf: false);

                var all = versionsTask.Result;
                var release = all.Where(v => v.VersionType == "release").ToList();
                var beta = all.Where(v => v.VersionType != "release").ToList();

                release = SortByPreference(release);
                beta = SortByPreference(beta);

                foreach (var v in release) ReleaseVersions.Add(v);
                foreach (var v in beta) BetaVersions.Add(v);
            }

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

    private List<ModrinthVersion> SortByPreference(List<ModrinthVersion> versions) =>
        versions
            .OrderByDescending(v => MatchScore(v))
            .ThenByDescending(v => ParseDate(v.DatePublished))
            .ToList();

    private int MatchScore(ModrinthVersion v)
    {
        int score = 0;
        if (!string.IsNullOrEmpty(_preferredGameVersion) &&
            v.GameVersions.Any(g => g.Equals(_preferredGameVersion, StringComparison.OrdinalIgnoreCase)))
            score += 2;
        if (!string.IsNullOrEmpty(_preferredLoader) &&
            v.Loaders.Any(l => l.Equals(_preferredLoader, StringComparison.OrdinalIgnoreCase)))
            score += 1;
        return score;
    }

    private static DateTime ParseDate(string s) =>
        DateTime.TryParse(s, out var dt) ? dt : DateTime.MinValue;

    private async Task LoadProjectIconAsync(ModrinthProject project, bool isCf)
    {
        var bmp = isCf
            ? await CurseForgeService.LoadIconAsync(project.IconUrl)
            : await ModrinthService.LoadIconAsync(project.IconUrl);
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
        try { if (CurrentProject != null) CurrentProject.IconBitmap = null; } catch { }
        CurrentProject = null;
        ReleaseVersions.Clear();
        BetaVersions.Clear();
        IsBetaExpanded = false;
        StatusMessage = "";
        ResultsUpdated?.Invoke();
    }

    [RelayCommand]
    private void ToggleBeta() => IsBetaExpanded = !IsBetaExpanded;

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
        var isCf = IsCurseForge || (IsMerged && CurrentProject != null && IsCfHit(new ModrinthSearchHit { ProjectId = CurrentProject.Id }));

        if (isCf && long.TryParse(version.Id, out var cfFileId) &&
            long.TryParse(CurrentProject?.Id, out var cfId))
        {
            await ShowSaveAndDownloadAsync(file, name, cfId: cfId, cfFileId: cfFileId);
        }
        else
        {
            await ShowSaveAndDownloadAsync(file, name);
        }
    }

    [RelayCommand]
    private async Task InstallModAsync(ModrinthSearchHit? hit)
    {
        if (hit == null) return;

        StatusMessage = $"正在获取「{hit.Title}」的版本...";

        try
        {
            var isCf = IsMerged ? IsCfHit(hit) : IsCurseForge;

            if (isCf)
            {
                if (!long.TryParse(hit.ProjectId, out var cfId)) return;

                var files = await CurseForgeService.GetFilesAsync(cfId);
                if (files.Count == 0)
                {
                    StatusMessage = $"「{hit.Title}」没有可用文件";
                    return;
                }

                var preferredGame = SelectedGameVersion == "全部" ? "" : SelectedGameVersion;
                var sorted = files
                    .OrderByDescending(f =>
                    {
                        int score = 0;
                        if (!string.IsNullOrEmpty(preferredGame) &&
                            f.GameVersions.Any(g => g.Equals(preferredGame, StringComparison.OrdinalIgnoreCase)))
                            score += 2;
                        return score;
                    })
                    .ThenByDescending(f => ParseDate(f.FileDate))
                    .ToList();

                var best = sorted[0];
                await ShowSaveAndDownloadAsync(
                    new ModrinthFile { Url = best.DownloadUrl ?? "", Filename = best.FileName, Size = best.FileLength, Primary = true },
                    hit.Title,
                    cfId: cfId,
                    cfFileId: best.Id);
            }
            else
            {
                var versions = await ModrinthService.GetVersionsAsync(
                    hit.ProjectId, gameVersion: null, loader: null);

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"安装失败：{ex.Message}";
        }
    }

    private async Task ShowSaveAndDownloadAsync(ModrinthFile file, string itemName,
        long cfId = 0, long cfFileId = 0)
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

            try { Directory.CreateDirectory(dir); startDir = dir; } catch { }
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

        try { Directory.CreateDirectory(targetDir); }
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

        try
        {
            await Task.Run(async () =>
            {
                if (cfId > 0 && cfFileId > 0)
                {
                    await CurseForgeService.DownloadFileToPathAsync(
                        cfId, cfFileId, targetPath,
                        (read, total) => ReportProgress(read, total, targetPath, localJob, globalJob, globalVm));
                }
                else
                {
                    await ModrinthService.DownloadFileToPathAsync(
                        file, targetPath,
                        (read, total) => ReportProgress(read, total, targetPath, localJob, globalJob, globalVm));
                }
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

            _ = Task.Delay(2000).ContinueWith(_ => globalVm.RemoveExternalJob(globalJob));
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

            _ = Task.Delay(3000).ContinueWith(_ => globalVm.RemoveExternalJob(globalJob));
        }
    }

    private long _prevBytes;
    private DateTime _lastTime = DateTime.UtcNow;

    private void ReportProgress(long read, long total, string targetPath,
        ModDownloadJob localJob, DownloadJob globalJob, DownloadViewModel globalVm)
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
            var dt = (now - _lastTime).TotalSeconds;
            if (dt >= 0.5)
            {
                long delta = read - _prevBytes;
                if (delta > 0)
                {
                    double speed = delta / dt;
                    globalJob.SpeedText = speed < 1024
                        ? $"{speed:F0} B/s"
                        : speed < 1024 * 1024
                            ? $"{speed / 1024:F1} KB/s"
                            : $"{speed / 1024 / 1024:F2} MB/s";
                }
                _prevBytes = read;
                _lastTime = now;
            }

            globalVm.NotifyExternalJobUpdated();
            StatusMessage = $"正在下载 {Path.GetFileName(targetPath)}... {pct:F0}%";
        });
    }
}