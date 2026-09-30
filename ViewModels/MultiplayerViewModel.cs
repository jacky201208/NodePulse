using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NodePulse.Services.Multiplayer;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace NodePulse.ViewModels;

public enum MultiplayerTabKind
{
    Terracotta,
    Linker
}

public partial class MultiplayerTabItem : ObservableObject
{
    public MultiplayerTabKind Kind { get; set; }
    public string DisplayName { get; set; } = "";

    [ObservableProperty] private bool _isSelected;
}

public partial class MultiplayerViewModel : ObservableObject
{
    private static MultiplayerViewModel? _instance;
    public static MultiplayerViewModel Instance => _instance ??= new MultiplayerViewModel();
    public static MultiplayerViewModel? Current => _instance;

    private readonly TerracottaService _service = new();
    private readonly LinkerService _linker = new();
    private bool _disposed;

    // ================================================================
    // Tab
    // ================================================================

    public ObservableCollection<MultiplayerTabItem> Tabs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTerracottaTab))]
    [NotifyPropertyChangedFor(nameof(IsLinkerTab))]
    private MultiplayerTabKind _selectedTab = MultiplayerTabKind.Terracotta;

    public bool IsTerracottaTab => SelectedTab == MultiplayerTabKind.Terracotta;
    public bool IsLinkerTab => SelectedTab == MultiplayerTabKind.Linker;

    private void InitTabs()
    {
        Tabs.Add(new MultiplayerTabItem
        {
            Kind = MultiplayerTabKind.Terracotta,
            DisplayName = "陶瓦联机",
            IsSelected = true
        });
        Tabs.Add(new MultiplayerTabItem
        {
            Kind = MultiplayerTabKind.Linker,
            DisplayName = "Linker联机"
        });
    }

    [RelayCommand]
    private void SelectTab(MultiplayerTabItem? tab)
    {
        if (tab == null) return;
        foreach (var t in Tabs) t.IsSelected = (t == tab);
        SelectedTab = tab.Kind;

        if (tab.Kind == MultiplayerTabKind.Linker)
            RefreshLinkerReady();
    }

    // ================================================================
    // 陶瓦联机
    // ================================================================

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _stateName = "";
    [ObservableProperty] private string _roomCode = "";
    [ObservableProperty] private string _inputRoomCode = "";
    [ObservableProperty] private string _playerName = "Player";
    [ObservableProperty] private string _statusText = "未启动";
    [ObservableProperty] private string _errorText = "";

    public bool IsStopped => !IsRunning;
    public bool IsIdle => IsRunning && StateName == TerracottaStateNames.Waiting;
    public bool IsBusy => StateName is TerracottaStateNames.HostScanning
                              or TerracottaStateNames.HostStarting
                              or TerracottaStateNames.GuestConnecting
                              or TerracottaStateNames.GuestStarting;
    public bool IsHostReady => StateName == TerracottaStateNames.HostOk;
    public bool IsGuestReady => StateName == TerracottaStateNames.GuestOk;
    public bool HasError => StateName == TerracottaStateNames.Exception;

    private MultiplayerViewModel()
    {
        InitTabs();
        _service.StateChanged += OnTerracottaStateChanged;
        _linker.LogReceived += s => System.Diagnostics.Debug.WriteLine($"[Linker] {s}");
    }

    private void OnTerracottaStateChanged(TerracottaState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StateName = state.State ?? "";
            RoomCode = state.Room ?? "";
            UpdateStatusText();
            RaiseDerived();
        });
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsHostReady));
        OnPropertyChanged(nameof(IsGuestReady));
        OnPropertyChanged(nameof(HasError));
    }

    private void UpdateStatusText()
    {
        StatusText = StateName switch
        {
            TerracottaStateNames.Waiting => "已就绪",
            TerracottaStateNames.HostScanning => "正在创建房间...",
            TerracottaStateNames.HostStarting => "正在启动房间...",
            TerracottaStateNames.HostOk => "房间已就绪",
            TerracottaStateNames.GuestConnecting => "正在连接房主...",
            TerracottaStateNames.GuestStarting => "正在建立连接...",
            TerracottaStateNames.GuestOk => "已成功连接",
            TerracottaStateNames.Exception => "出现错误",
            "" => "未启动",
            _ => StateName
        };
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning) return;
        ErrorText = "";
        StatusText = "正在启动陶瓦联机...";
        try
        {
            await _service.StartAsync();
            IsRunning = true;
            RaiseDerived();
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = $"启动失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BeHostAsync()
    {
        ErrorText = "";
        try
        {
            await _service.SetScanningAsync(
                string.IsNullOrWhiteSpace(PlayerName) ? null : PlayerName);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BeGuestAsync()
    {
        ErrorText = "";
        if (string.IsNullOrWhiteSpace(InputRoomCode))
        {
            StatusText = "请先输入邀请码";
            return;
        }

        try
        {
            await _service.SetGuestingAsync(
                InputRoomCode.Trim(),
                string.IsNullOrWhiteSpace(PlayerName) ? null : PlayerName);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            StatusText = $"加入失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BackToIdleAsync()
    {
        try { await _service.SetWaitingAsync(); }
        catch (Exception ex) { ErrorText = ex.Message; }
    }

    // ================================================================
    // Linker 联机
    // ================================================================

    [ObservableProperty] private bool _linkerIsRunning;
    [ObservableProperty] private string _linkerStatus = "未启动";
    [ObservableProperty] private string _linkerError = "";
    [ObservableProperty] private bool _linkerIsBusy;
    [ObservableProperty] private string _linkerWebUrl = "";

    /// <summary>是否已下载 Linker 组件</summary>
    [ObservableProperty] private bool _linkerIsReady;

    /// <summary>下载进度 0-100</summary>
    [ObservableProperty] private double _linkerDownloadProgress;

    /// <summary>是否正在下载</summary>
    [ObservableProperty] private bool _linkerIsDownloading;

    public bool LinkerIsIdle => !LinkerIsRunning;

    /// <summary>需要下载（未就绪且不在下载中）</summary>
    public bool LinkerNeedsDownload => !LinkerIsReady && !LinkerIsDownloading;

    /// <summary>已就绪且未运行（可以点启动）</summary>
    public bool LinkerCanStart => LinkerIsReady && !LinkerIsRunning;

    partial void OnLinkerIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(LinkerIsIdle));
        OnPropertyChanged(nameof(LinkerCanStart));
    }

    partial void OnLinkerIsReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(LinkerNeedsDownload));
        OnPropertyChanged(nameof(LinkerCanStart));
    }

    partial void OnLinkerIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(LinkerNeedsDownload));
    }

    /// <summary>刷新 Linker 就绪状态</summary>
    public void RefreshLinkerReady()
    {
        try
        {
            LinkerIsReady = LinkerBinaryManager.IsReady();
        }
        catch
        {
            LinkerIsReady = false;
        }
    }

    // ----------------------------------------------------------------
    // 一键下载
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task LinkerDownloadAsync()
    {
        if (LinkerIsDownloading) return;

        LinkerIsDownloading = true;
        LinkerIsReady = false;
        LinkerError = "";
        LinkerDownloadProgress = 0;

        try
        {
            var url = LinkerBinaryManager.GetDownloadUrl();
            if (string.IsNullOrEmpty(url))
            {
                LinkerError = "当前平台暂不支持自动下载，请手动下载。";
                return;
            }

            LinkerStatus = $"正在下载 Linker ({LinkerBinaryManager.LinkerVersion})...";

            var progress = new Progress<double>(p =>
            {
                LinkerDownloadProgress = p;
                if (p < 80)
                    LinkerStatus = $"正在下载... {p:F0}%";
                else if (p < 100)
                    LinkerStatus = "正在解压...";
            });

            await Task.Run(async () =>
            {
                await LinkerBinaryManager.DownloadAndExtractAsync(progress);
            });

            LinkerIsReady = true;
            LinkerDownloadProgress = 100;
            LinkerStatus = "✅ Linker 下载完成，可以启动。";
        }
        catch (Exception ex)
        {
            LinkerError = $"下载失败：{ex.Message}";
            LinkerStatus = "下载失败";
        }
        finally
        {
            LinkerIsDownloading = false;
            RefreshLinkerReady();
        }
    }

    // ----------------------------------------------------------------
    // 启动
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task LinkerStartAsync()
    {
        if (LinkerIsRunning) return;

        LinkerIsBusy = true;
        LinkerError = "";
        LinkerStatus = "正在启动 Linker（请在弹出的 UAC 对话框中点击「是」）...";

        try
        {
            await _linker.StartAsync();

            LinkerIsRunning = true;
            LinkerWebUrl = _linker.WebUrl;
            LinkerStatus = $"已启动，管理界面：{_linker.WebUrl}";

            // 自动打开浏览器
            _linker.OpenWebUi();
        }
        catch (Exception ex)
        {
            LinkerError = ex.Message;
            LinkerStatus = "启动失败";
        }
        finally
        {
            LinkerIsBusy = false;
        }
    }

    // ----------------------------------------------------------------
    // 重启（首次初始化 / 配置改动后重新加载）
    // ----------------------------------------------------------------

    [RelayCommand]
    private async Task LinkerRestartAsync()
    {
        if (LinkerIsBusy) return;

        LinkerIsBusy = true;
        LinkerError = "";
        LinkerStatus = "正在重启 Linker...";

        try
        {
            // 1. 停止当前进程
            try { _linker.Stop(); } catch { }
            LinkerIsRunning = false;
            LinkerWebUrl = "";

            // 2. 等待端口释放
            await Task.Delay(1500);

            // 3. 重新启动
            await _linker.StartAsync();

            LinkerIsRunning = true;
            LinkerWebUrl = _linker.WebUrl;
            LinkerStatus = $"已重启，管理界面：{_linker.WebUrl}";

            // 4. 稍等片刻再打开浏览器，确保 Web UI 起来
            await Task.Delay(800);
            _linker.OpenWebUi();
        }
        catch (Exception ex)
        {
            LinkerError = ex.Message;
            LinkerStatus = "重启失败";
        }
        finally
        {
            LinkerIsBusy = false;
        }
    }

    [RelayCommand]
    private void LinkerOpenWebUi()
    {
        if (!LinkerIsRunning) return;
        _linker.OpenWebUi();
    }

    /// <summary>
    /// 打开 Linker 组件目录
    /// </summary>
    [RelayCommand]
    private void LinkerOpenFolder()
    {
        try
        {
            var dir = LinkerBinaryManager.GetLinkerDir();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", dir);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", dir);
            }
        }
        catch (Exception ex)
        {
            LinkerError = $"打开目录失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LinkerStopAsync()
    {
        try
        {
            _linker.Stop();
        }
        catch { }

        LinkerIsRunning = false;
        LinkerStatus = "已停止";
        LinkerWebUrl = "";
        await Task.CompletedTask;
    }

    // ================================================================
    // 关闭
    // ================================================================

    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;

        _service.StateChanged -= OnTerracottaStateChanged;
        _service.Dispose();

        _linker.Dispose();
    }
}