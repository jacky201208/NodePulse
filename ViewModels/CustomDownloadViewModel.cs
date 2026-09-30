using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodePulse.Models;
using NodePulse.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NodePulse.ViewModels;

public partial class CustomDownloadViewModel : ObservableObject
{
    public IDialogService? DialogService { get; set; }
    public event Action<string>? NotificationRequested;

    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _saveDirectory = "";
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _isDownloading;

    /// <summary>记录上一次自动提取文件名的 URL，用于判断用户是否手动改过 FileName</summary>
    private string _lastAutoUrl = "";

    /// <summary>用于防止初始化时反复写盘</summary>
    private bool _loaded;

    // ----------------------------------------------------------------
    // 构造：读取上次保存目录
    // ----------------------------------------------------------------

    public CustomDownloadViewModel()
    {
        try
        {
            var g = GlobalSettings.Load();
            if (!string.IsNullOrWhiteSpace(g.CustomDownloadSaveDir))
                SaveDirectory = g.CustomDownloadSaveDir;
        }
        catch { }

        _loaded = true;
    }

    // ----------------------------------------------------------------
    // URL 变化时自动提取文件名
    // ----------------------------------------------------------------

    partial void OnUrlChanged(string value)
    {
        var autoName = ExtractFileNameFromUrl(value);
        if (string.IsNullOrEmpty(autoName)) return;

        var previousAutoName = ExtractFileNameFromUrl(_lastAutoUrl);

        if (string.IsNullOrWhiteSpace(FileName) || FileName == previousAutoName)
        {
            _lastAutoUrl = value;
            FileName = autoName;
        }
    }

    // ----------------------------------------------------------------
    // 保存目录变化时写盘
    // ----------------------------------------------------------------

    partial void OnSaveDirectoryChanged(string value)
    {
        if (!_loaded) return;

        try
        {
            var g = GlobalSettings.Load();
            g.CustomDownloadSaveDir = value ?? "";
            g.Save();
        }
        catch { }
    }

    private static string ExtractFileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.AbsolutePath);
            return string.IsNullOrEmpty(name) ? "" : Uri.UnescapeDataString(name);
        }
        catch
        {
            return "";
        }
    }

    // ----------------------------------------------------------------
    // 浏览保存目录
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseSaveDirectoryAsync()
    {
        if (DialogService == null) return;

        var dir = await DialogService.PickFolderAsync("选择保存目录");
        if (!string.IsNullOrEmpty(dir))
            SaveDirectory = dir;
    }

    // ----------------------------------------------------------------
    // 打开保存目录
    // ----------------------------------------------------------------

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            string dir = SaveDirectory;

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                dir = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
            }

            if (!Directory.Exists(dir))
            {
                StatusMessage = "目录不存在";
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", dir);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", dir);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开目录失败：{ex.Message}";
        }
    }

    // ----------------------------------------------------------------
    // 开始下载
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task StartDownloadAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            StatusMessage = "请输入下载链接";
            return;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            StatusMessage = "请选择保存目录";
            return;
        }

        if (string.IsNullOrWhiteSpace(FileName))
        {
            StatusMessage = "请输入文件名";
            return;
        }

        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            StatusMessage = "链接格式不正确";
            return;
        }

        try
        {
            Directory.CreateDirectory(SaveDirectory);
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建目录失败：{ex.Message}";
            return;
        }

        var targetPath = Path.Combine(SaveDirectory, FileName);
        var targetDir = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrEmpty(targetDir))
        {
            StatusMessage = "目标路径无效";
            return;
        }

        IsDownloading = true;
        Percent = 0;
        StatusMessage = "正在下载...";

        var globalVm = DownloadViewModel.Instance;

        var globalJob = new DownloadJob
        {
            VersionId = FileName,
            VersionType = "自定义",
            VersionName = FileName,
            State = DownloadJobState.Running,
            Cts = new System.Threading.CancellationTokenSource(),
            Stage = "准备下载...",
            StageDescription = "准备下载...",
            LastSpeedTime = DateTime.UtcNow,
            LastByteChangeTime = DateTime.UtcNow
        };

        globalVm.AddExternalJob(globalJob);

        long prevBytes = 0;
        var lastTime = DateTime.UtcNow;

        try
        {
            await Task.Run(async () =>
            {
                await ModrinthService.DownloadUrlToPathAsync(
                    Url,
                    targetPath,
                    (read, total) =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            double pct = total > 0 ? (double)read / total * 100 : 0;

                            Percent = pct;

                            globalJob.TotalPercent = pct;
                            globalJob.StagePercent = pct;
                            globalJob.BytesPercent = pct;
                            globalJob.CurrentFile = FileName;
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

                            StatusMessage = $"正在下载... {pct:F0}%";
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

            Percent = 100;
            StatusMessage = $"✅ 下载完成：{FileName}";
            NotificationRequested?.Invoke($"「{FileName}」下载完成");

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
            globalVm.NotifyExternalJobUpdated();

            StatusMessage = $"❌ 下载失败：{ex.Message}";
            NotificationRequested?.Invoke($"下载失败：{ex.Message}");

            _ = Task.Delay(3000).ContinueWith(_ =>
            {
                globalVm.RemoveExternalJob(globalJob);
            });
        }
        finally
        {
            IsDownloading = false;
        }
    }
}