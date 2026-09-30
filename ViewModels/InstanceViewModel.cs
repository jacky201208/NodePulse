using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
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

        public InstanceViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            LaunchInstanceCommand = new AsyncRelayCommand<MinecraftInstance>(LaunchInstanceAsync);
            OpenSettingsCommand = new RelayCommand<MinecraftInstance>(OpenSettings);
            DeleteInstanceCommand = new AsyncRelayCommand<MinecraftInstance>(DeleteInstanceAsync);

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

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }
    }
}