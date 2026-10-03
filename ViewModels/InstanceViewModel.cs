using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    /// <summary>已导入 Minecraft 文件夹的一条记录</summary>
    public class FolderEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        /// <summary>完整路径</summary>
        public string Path { get; }

        /// <summary>显示用的文件夹名称</summary>
        public string Name { get; }

        private bool _isCurrent;
        /// <summary>是否为当前正在使用的文件夹</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value) return;
                _isCurrent = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCurrent)));
            }
        }

        public FolderEntry(string path, bool isCurrent)
        {
            Path = path;
            var trimmed = path.TrimEnd('\\', '/');
            Name = System.IO.Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(Name)) Name = path;
            _isCurrent = isCurrent;
        }
    }

    public class InstanceViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        public event Action<string>? SettingsRequested;

        public ObservableCollection<MinecraftInstance> Instances { get; } = new();

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private MinecraftInstance? _selectedInstance;
        public MinecraftInstance? SelectedInstance
        {
            get => _selectedInstance;
            set => SetProperty(ref _selectedInstance, value);
        }

        public AsyncRelayCommand RefreshCommand { get; }
        public AsyncRelayCommand<MinecraftInstance> LaunchInstanceCommand { get; }
        public RelayCommand<MinecraftInstance> OpenSettingsCommand { get; }
        public AsyncRelayCommand<MinecraftInstance> DeleteInstanceCommand { get; }

        // ================================================================
        // 实例菜单：当前文件夹 / 导入
        // ================================================================

        public AsyncRelayCommand ImportFolderCommand { get; }
        public AsyncRelayCommand ImportPackCommand { get; }

        public string MinecraftFolderPath => VersionScanner.MinecraftFolder;

        public string MinecraftFolderLabel =>
            $"当前 Minecraft 文件夹：{VersionScanner.MinecraftFolder}";

        /// <summary>已导入过的 Minecraft 文件夹列表</summary>
        public ObservableCollection<FolderEntry> MinecraftFolders { get; } = new();

        /// <summary>切换当前 Minecraft 文件夹</summary>
        public RelayCommand<string> SwitchFolderCommand { get; }

        public InstanceViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            LaunchInstanceCommand = new AsyncRelayCommand<MinecraftInstance>(LaunchInstanceAsync);
            OpenSettingsCommand = new RelayCommand<MinecraftInstance>(OpenSettings);
            DeleteInstanceCommand = new AsyncRelayCommand<MinecraftInstance>(DeleteInstanceAsync);

            ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync);
            ImportPackCommand = new AsyncRelayCommand(ImportPackAsync);
            SwitchFolderCommand = new RelayCommand<string>(SwitchFolder);

            LoadFolders();

            _ = RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            StatusText = "正在扫描实例...";

            // 后台线程：只解析 JSON，不加载图标
            var list = await Task.Run(() => MinecraftInstance.ScanAll());

            // ★ 回到 UI 线程：清空 + 加载图标 + 加入列表
            Instances.Clear();
            foreach (var item in list)
            {
                item.LoadIcon();     // 必须 UI 线程
                Instances.Add(item);
            }

            StatusText = list.Count == 0
                ? "没有已安装的实例"
                : $"共 {list.Count} 个实例";

            OnPropertyChanged(nameof(MinecraftFolderPath));
            OnPropertyChanged(nameof(MinecraftFolderLabel));
        }

        // ================================================================
        // 导入 Minecraft 文件夹
        // ================================================================

        private async Task ImportFolderAsync()
        {
            var path = await _dialogService.PickFolderAsync("选择要导入的 Minecraft 文件夹（.minecraft）");
            if (string.IsNullOrEmpty(path)) return;

            var mcDir = path;

            // 用户可能选中的是 .minecraft 的父目录
            var sub = Path.Combine(path, ".minecraft");
            if (Directory.Exists(sub))
                mcDir = sub;

            if (!Directory.Exists(Path.Combine(mcDir, "versions")))
            {
                var confirm = await _dialogService.ShowConfirmAsync(
                    "导入 Minecraft 文件夹",
                    $"所选目录：\n{mcDir}\n\n未检测到 versions 文件夹，可能不是标准 Minecraft 目录。\n\n仍要将其设为 Minecraft 文件夹吗？");

                if (!confirm) return;
            }

            // 更新内存缓存 + 持久化
            VersionScanner.MinecraftFolder = mcDir;
            var g = GlobalSettings.Load();
            g.MinecraftFolder = mcDir;
            g.Save();

            OnPropertyChanged(nameof(MinecraftFolderPath));
            OnPropertyChanged(nameof(MinecraftFolderLabel));

            StatusText = $"已切换到 Minecraft 文件夹：{mcDir}";

            // 登记到已导入列表（去重）+ 更新高亮
            if (!MinecraftFolders.Any(f => string.Equals(f.Path, mcDir, StringComparison.OrdinalIgnoreCase)))
                MinecraftFolders.Add(new FolderEntry(mcDir, true));
            ApplyCurrentMark();

            await RefreshAsync();
        }

        // ================================================================
        // 导入整合包
        // ================================================================

        private async Task ImportPackAsync()
        {
            var file = await _dialogService.PickFileAsync(
                "选择整合包 (.zip)", new[] { "zip" });
            if (string.IsNullOrEmpty(file)) return;

            StatusText = "正在导入整合包...";
            var mcDir = VersionScanner.MinecraftFolder;

            try
            {
                var count = await Task.Run(() => ExtractPackZip(file, mcDir));

                if (count == 0)
                {
                    StatusText = "整合包内未发现版本文件";
                    await _dialogService.ShowMessageAsync("导入整合包",
                        "包内未提取到任何文件。可能该压缩包不是 NodePulse 可识别的整合包格式。");
                    return;
                }

                StatusText = $"✅ 已导入 {count} 个文件";
                await RefreshAsync();
                StatusText = $"✅ 已导入 {count} 个文件（当前共 {Instances.Count} 个实例）";
            }
            catch (Exception ex)
            {
                StatusText = "导入失败";
                await _dialogService.ShowMessageAsync("导入失败",
                    $"导入整合包时出错：\n{ex.Message}");
            }
        }

        /// <summary>解压整合包 .zip 到 Minecraft 文件夹，返回成功提取的文件数。</summary>
        private static int ExtractPackZip(string zipPath, string mcDir)
        {
            var root = Path.GetFullPath(mcDir)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            var count = 0;
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                // 跳过目录条目和压缩空条目
                if (string.IsNullOrEmpty(entry.Name)) continue;

                // 归一化相对路径，防目录穿越
                var rel = entry.FullName.Replace('\\', '/').TrimStart('/');
                var dest = Path.GetFullPath(Path.Combine(mcDir, rel));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;

                var dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                entry.ExtractToFile(dest, overwrite: true);
                count++;
            }

            return count;
        }

        private async Task LaunchInstanceAsync(MinecraftInstance? instance)
        {
            if (instance == null) return;
            StatusText = $"正在启动 {instance.Name} ...";
            try
            {
                var name = instance.Name;
                var proc = await Task.Run(() => MinecraftLauncher.Launch(name));
                StatusText = $"已启动 {name} (PID: {proc.Id})";
                await _dialogService.ShowMessageAsync("NodePulse",
                    $"已启动 {name}\n进程ID: {proc.Id}");
            }
            catch (Exception ex)
            {
                StatusText = "启动失败";
                await _dialogService.ShowMessageAsync("错误", $"启动失败：{ex.Message}");
            }
        }

        private void OpenSettings(MinecraftInstance? instance)
        {
            if (instance == null) return;
            SettingsRequested?.Invoke(instance.Name);
        }

        private async Task DeleteInstanceAsync(MinecraftInstance? instance)
        {
            if (instance == null) return;

            var name = instance.Name;
            var sizeText = FormatSize(instance.TotalSize);

            var confirmed = await _dialogService.ShowConfirmAsync(
                "删除实例",
                $"确定要删除实例 \"{name}\" 吗？\n\n" +
                $"将删除目录：\n.minecraft\\versions\\{name}\\\n\n" +
                $"占用空间：{sizeText}\n\n" +
                $"此操作不可恢复！");

            if (!confirmed) return;

            StatusText = $"正在删除 {name} ...";

            try
            {
                var versionDir = Path.Combine(
                    VersionScanner.MinecraftFolder, "versions", name);

                if (!Directory.Exists(versionDir))
                {
                    StatusText = $"目录不存在：{versionDir}";
                    await RefreshAsync();
                    return;
                }

                await Task.Run(() => Directory.Delete(versionDir, recursive: true));

                StatusText = $"✅ 已删除 {name}";

                await RefreshAsync();
                StatusText = $"✅ 已删除 {name}（剩余 {Instances.Count} 个实例）";
            }
            catch (UnauthorizedAccessException)
            {
                StatusText = "删除失败：权限不足";
                await _dialogService.ShowMessageAsync(
                    "删除失败",
                    $"无法删除 \"{name}\"。\n\n" +
                    "可能原因：\n" +
                    "• 该实例正在被游戏进程占用\n" +
                    "• 文件被其他程序锁定\n" +
                    "• 目录权限不足");
            }
            catch (IOException ex)
            {
                StatusText = "删除失败：文件被占用";
                await _dialogService.ShowMessageAsync(
                    "删除失败",
                    $"无法删除 \"{name}\"。\n\n" +
                    $"错误：{ex.Message}\n\n" +
                    "请关闭正在运行的游戏后重试。");
            }
            catch (Exception ex)
            {
                StatusText = "删除失败";
                await _dialogService.ShowMessageAsync("删除失败", ex.Message);
            }
        }

        // ================================================================
        // 已导入 Minecraft 文件夹列表：加载 / 切换
        // ================================================================

        private void LoadFolders()
        {
            var g = GlobalSettings.Load();
            if (g.MinecraftFolders.Count == 0 && !string.IsNullOrEmpty(g.MinecraftFolder))
            {
                // 兼容旧版：没有历史列表时，把当前文件夹作为首项
                g.MinecraftFolders.Add(g.MinecraftFolder);
            }

            MinecraftFolders.Clear();
            foreach (var p in g.MinecraftFolders)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                MinecraftFolders.Add(new FolderEntry(p, false));
            }

            // 确保当前文件夹在列表中
            var cur = VersionScanner.MinecraftFolder;
            if (!string.IsNullOrEmpty(cur) &&
                !MinecraftFolders.Any(f => string.Equals(f.Path, cur, StringComparison.OrdinalIgnoreCase)))
            {
                MinecraftFolders.Add(new FolderEntry(cur, true));
            }

            ApplyCurrentMark();
        }

        private void SwitchFolder(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Directory.Exists(path))
            {
                StatusText = "文件夹不存在，无法切换";
                return;
            }
            if (string.Equals(VersionScanner.MinecraftFolder, path, StringComparison.OrdinalIgnoreCase))
                return;

            VersionScanner.MinecraftFolder = path;

            var g = GlobalSettings.Load();
            g.MinecraftFolder = path;
            g.Save();

            ApplyCurrentMark();
            OnPropertyChanged(nameof(MinecraftFolderPath));
            OnPropertyChanged(nameof(MinecraftFolderLabel));

            StatusText = $"已切换到 Minecraft 文件夹：{path}";
            _ = RefreshAsync();
        }

        /// <summary>刷新列表各条的“当前”高亮标记</summary>
        private void ApplyCurrentMark()
        {
            var cur = VersionScanner.MinecraftFolder;
            foreach (var f in MinecraftFolders)
                f.IsCurrent = string.Equals(f.Path, cur, StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }
    }
}