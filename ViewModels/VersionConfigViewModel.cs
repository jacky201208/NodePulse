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

public partial class VersionConfigViewModel : ObservableObject
{
    private readonly VersionInfo _version;

    public string BaseVersion => _version.Id;

    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private bool _hasNameConflict;
    [ObservableProperty] private string _conflictMessage = "";

    public ObservableCollection<ModLoaderOption> Loaders { get; } = new();

    public event Action<ModLoaderConfig>? DownloadRequested;
    public event Action? Cancelled;

    public VersionConfigViewModel(VersionInfo version)
    {
        _version = version;
        GameName = version.Id;

        BuildLoaders();
        CheckNameConflict();

        // ★ 每个加载器选项的 SelectedVersion 变化时都要重算 GameName
        foreach (var l in Loaders)
            l.PropertyChanged += OnLoaderPropertyChanged;

        _ = LoadAllVersionsAsync();
    }

    private void OnLoaderPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModLoaderOption.SelectedVersion))
        {
            // 只有当前选中的加载器变化了才需要更新
            if (sender is ModLoaderOption opt && opt.IsSelected)
                UpdateDefaultGameName();
        }
    }

    partial void OnGameNameChanged(string value) => CheckNameConflict();

    private void CheckNameConflict()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(GameName))
            {
                HasNameConflict = false;
                ConflictMessage = "";
                return;
            }

            var dir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", GameName);

            if (Directory.Exists(dir))
            {
                HasNameConflict = true;
                ConflictMessage = "已存在同名游戏";
            }
            else
            {
                HasNameConflict = false;
                ConflictMessage = "";
            }
        }
        catch
        {
            HasNameConflict = false;
            ConflictMessage = "";
        }
    }

    private void BuildLoaders()
    {
        Loaders.Clear();

        var mcVer = _version.Id;

        Loaders.Add(new ModLoaderOption
        {
            Type = ModLoaderType.None,
            DisplayName = "原版",
            Description = "不安装模组加载器",
            IconBg = "#3D5A2C",
            IconBitmap = IconLoader.Load("vanilla.png"),
            IsSelected = true,
            VersionsLoaded = true
        });

        TryAddLoader(ModLoaderType.Fabric, "Fabric",
            "支持选择 Fabric 版本并安装",
            "#5A4633", "fabric.png", mcVer);

        TryAddLoader(ModLoaderType.Forge, "Forge",
            "支持选择 Forge 版本并安装",
            "#3A4145", "forge.png", mcVer);

        TryAddLoader(ModLoaderType.NeoForge, "NeoForge",
            "支持选择 NeoForge 版本并安装",
            "#5A2E0A", "neoforge.png", mcVer);

        TryAddLoader(ModLoaderType.Quilt, "Quilt",
            "支持选择 Quilt 版本并安装",
            "#3F2F55", "quilt.png", mcVer);

        TryAddLoader(ModLoaderType.OptiFine, "OptiFine",
            "支持选择 OptiFine 版本并安装",
            "#5A3D08", "optifine.png", mcVer);
    }

    private void TryAddLoader(ModLoaderType type, string name, string desc,
        string iconBg, string iconFile, string mcVer)
    {
        if (!ModLoaderCompatibility.Supports(mcVer, type)) return;

        var opt = new ModLoaderOption
        {
            Type = type,
            DisplayName = name,
            Description = desc,
            IconBg = iconBg,
            IconBitmap = IconLoader.Load(iconFile),
            IsLoadingVersions = true,
            LoadingText = $"正在读取 {name} 版本...",
            ShowApiSelector = (type == ModLoaderType.Fabric)
        };

        Loaders.Add(opt);
    }

    private async Task LoadAllVersionsAsync()
    {
        var mcVer = _version.Id;

        var tasks = Loaders
            .Where(l => l.Type != ModLoaderType.None)
            .Select(l => LoadLoaderVersionsAsync(l, mcVer))
            .ToList();

        await Task.WhenAll(tasks);
    }

    private async Task LoadLoaderVersionsAsync(ModLoaderOption opt, string mcVer)
    {
        try
        {
            switch (opt.Type)
            {
                case ModLoaderType.Fabric:
                {
                    var loaderTask = ModLoaderService.GetFabricLoaderVersionsAsync(mcVer);
                    var apiTask = ModLoaderService.GetFabricApiVersionsAsync(mcVer);
                    await Task.WhenAll(loaderTask, apiTask);

                    var versions = await WithFallback(
                        Task.FromResult(loaderTask.Result), mcVer, opt.Type);

                    FillVersions(opt, versions);
                    FillApiVersions(opt, apiTask.Result);
                    break;
                }

                case ModLoaderType.Quilt:
                {
                    var versions = await WithFallback(
                        ModLoaderService.GetQuiltLoaderVersionsAsync(mcVer),
                        mcVer, opt.Type);
                    FillVersions(opt, versions);
                    break;
                }

                case ModLoaderType.Forge:
                {
                    var versions = await WithFallback(
                        ModLoaderService.GetForgeVersionsAsync(mcVer),
                        mcVer, opt.Type);
                    FillVersions(opt, versions);
                    break;
                }

                case ModLoaderType.NeoForge:
                {
                    var versions = await WithFallback(
                        ModLoaderService.GetNeoForgeVersionsAsync(mcVer),
                        mcVer, opt.Type);
                    FillVersions(opt, versions);
                    break;
                }

                case ModLoaderType.OptiFine:
                {
                    var versions = await WithFallback(
                        ModLoaderService.GetOptiFineVersionsAsync(mcVer),
                        mcVer, opt.Type);
                    FillVersions(opt, versions);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            opt.VersionsLoaded = false;
            opt.IsLoadingVersions = false;
            opt.LoadingText = $"加载失败：{ex.Message}";
        }
    }

    private static async Task<List<string>> WithFallback(
        Task<List<string>> fetchTask, string mcVer, ModLoaderType type)
    {
        try
        {
            var list = await fetchTask;
            if (list != null && list.Count > 0)
                return list;
        }
        catch { }

        return ModLoaderCompatibility.GetVersions(mcVer, type);
    }

    private static void FillVersions(ModLoaderOption opt, List<string> versions)
    {
        opt.AvailableVersions.Clear();
        foreach (var v in versions)
            opt.AvailableVersions.Add(v);

        if (versions.Count > 0)
        {
            opt.SelectedVersion = versions[0];
            opt.VersionsLoaded = true;
            opt.IsLoadingVersions = false;
        }
        else
        {
            opt.VersionsLoaded = false;
            opt.IsLoadingVersions = false;
            opt.LoadingText = "暂无可用版本";
        }
    }

    private static void FillApiVersions(ModLoaderOption opt, List<string> versions)
    {
        opt.AvailableApiVersions.Clear();
        foreach (var v in versions)
            opt.AvailableApiVersions.Add(v);

        if (versions.Count > 0)
            opt.SelectedApiVersion = versions[0];

        opt.IsLoadingApi = false;
    }

    [RelayCommand]
    private void SelectLoader(ModLoaderOption? option)
    {
        if (option == null) return;

        foreach (var l in Loaders)
            l.IsSelected = (l == option);

        UpdateDefaultGameName();
    }

    private void UpdateDefaultGameName()
    {
        var selected = Loaders.FirstOrDefault(l => l.IsSelected);
        if (selected == null) return;

        if (selected.Type == ModLoaderType.None)
        {
            GameName = BaseVersion;
        }
        else if (!string.IsNullOrEmpty(selected.SelectedVersion))
        {
            var loaderName = selected.DisplayName.Replace(" ", "");
            GameName = $"{BaseVersion}-{loaderName}_{selected.SelectedVersion}";
        }
        // ★ 如果版本还没加载完，保持原样，等加载完 propertyChanged 再更新
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();

    [RelayCommand]
    private void StartDownload()
    {
        var selected = Loaders.FirstOrDefault(l => l.IsSelected)
                       ?? Loaders.FirstOrDefault();

        if (selected != null &&
            selected.Type != ModLoaderType.None &&
            !selected.VersionsLoaded)
        {
            return;
        }

        // ★ 最终校验：如果选了非原版加载器，GameName 不能等于 BaseVersion
        var finalName = string.IsNullOrWhiteSpace(GameName) ? BaseVersion : GameName;
        if (selected != null && selected.Type != ModLoaderType.None)
        {
            if (string.Equals(finalName, BaseVersion, StringComparison.OrdinalIgnoreCase))
            {
                // 这种情况会导致覆盖原版目录，拒绝
                return;
            }
        }

        var config = new ModLoaderConfig
        {
            BaseVersion = BaseVersion,
            FinalVersionName = finalName,
            SelectedLoader = selected?.Type ?? ModLoaderType.None,
            LoaderVersion = selected?.SelectedVersion ?? "",
            InstallFabricApi = selected?.Type == ModLoaderType.Fabric
                               && selected.InstallFabricApi,
            FabricApiVersion = selected?.SelectedApiVersion ?? ""
        };

        DownloadRequested?.Invoke(config);
    }
}