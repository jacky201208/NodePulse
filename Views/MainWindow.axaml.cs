using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using NodePulse.Services;
using NodePulse.ViewModels;
using NodePulse.Views;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse;

public partial class MainWindow : Window
{
    private readonly IDialogService _dialogService;
    private readonly MainWindowViewModel _mainVm;
    private readonly SemaphoreSlim _switchSemaphore = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _toastCts;
    private LaunchViewModel? _launchVm;
    private InstanceViewModel? _instanceVm;
    private AccountViewModel? _accountVm;
    private GlobalSettingsViewModel? _globalSettingsVm;

    // 缓存的 TransformOperations
    private static readonly Avalonia.Media.Transformation.TransformOperations
        ToastHidden = Avalonia.Media.Transformation.TransformOperations
            .Parse("translateY(20px)");

    private static readonly Avalonia.Media.Transformation.TransformOperations
        ToastVisible = Avalonia.Media.Transformation.TransformOperations
            .Parse("translateY(0px)");

    private static readonly Avalonia.Media.Transformation.TransformOperations
        PageIncoming = Avalonia.Media.Transformation.TransformOperations
            .Parse("translateY(40px)");

    private static readonly Avalonia.Media.Transformation.TransformOperations
        PageSettled = Avalonia.Media.Transformation.TransformOperations
            .Parse("translateY(0px)");

    public MainWindow()
    {
        InitializeComponent();

        _dialogService = new AvaloniaDialogService(this);

        _mainVm = new MainWindowViewModel(_dialogService);
        _mainVm.NavigationRequested += async (view) => await SwitchPageAsync(view);

        DataContext = DownloadViewModel.Instance;

        DownloadViewModel.Instance.NotificationRequested += ShowToast;

        // 给下载页所有 Tab 注入对话框服务和 Toast 通知
        DownloadViewModel.Instance.InjectDialogService(_dialogService);
        DownloadViewModel.Instance.InjectNotificationHandler(ShowToast);

        TitleBarDragArea.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (e.Source is Control control)
            {
                var btn = control.FindAncestorOfType<Button>();
                if (btn != null) return;
            }

            BeginMoveDrag(e);
        };

        BtnMin.Click += (_, _) => WindowState = WindowState.Minimized;
        BtnClose.Click += (_, _) => Close();

        // 启动器退出时清理 terracotta 进程
        Closing += OnWindowClosing;

        NavLaunch.Click += async (_, _) => await SwitchPageAsync(nameof(LaunchView));
        NavInstance.Click += async (_, _) => await SwitchPageAsync(nameof(InstanceView));
        NavDownload.Click += async (_, _) => await SwitchPageAsync(nameof(DownloadView));
        NavMultiplayer.Click += async (_, _) => await SwitchPageAsync(nameof(MultiplayerView));
        NavAccount.Click += async (_, _) => await SwitchPageAsync(nameof(AccountView));
        NavGlobalSettings.Click += async (_, _) => await SwitchPageAsync(nameof(GlobalSettingsView));
        NavAbout.Click += async (_, _) => await SwitchPageAsync(nameof(AboutView));

        _ = SwitchPageAsync(nameof(LaunchView));
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        try
        {
            MultiplayerViewModel.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private async void ShowToast(string message)
    {
        try
        {
            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ToastText.Text = message;
                ToastHost.Opacity = 1;
                ToastHost.RenderTransform = ToastVisible;
            });

            await Task.Delay(3000, token);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ToastHost.Opacity = 0;
                ToastHost.RenderTransform = ToastHidden;
            });
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    private static void LogError(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "ui.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{ex}{Environment.NewLine}");
        }
        catch { }
    }

    private void OnFloatingButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        DownloadViewModel.Instance.OpenPanelCommand.Execute(null);
    }

    private async Task SwitchPageAsync(string viewName)
    {
        await _switchSemaphore.WaitAsync();
        try
        {
            await SwitchPageCoreAsync(viewName);
        }
        finally
        {
            _switchSemaphore.Release();
        }
    }

    private async Task OpenSettingsAsync(string versionName, string returnToView)
    {
        var svm = new SettingsViewModel(_dialogService);
        svm.ReturnRequested += async () =>
        {
            await SwitchPageAsync(returnToView);
        };

        MainContent.Content = new SettingsView { DataContext = svm };
        await svm.LoadDataAsync();
        await svm.SelectVersionAsync(versionName);
    }

    private async Task SwitchPageCoreAsync(string viewName)
    {
        // 淡出 + 下移
        try
        {
            MainContent.Opacity = 0;
            MainContent.RenderTransform = PageIncoming;
        }
        catch (Exception ex) { LogError(ex); }

        await Task.Delay(240);

        switch (viewName)
        {
            case nameof(LaunchView):
            {
                if (_launchVm == null)
                {
                    _launchVm = new LaunchViewModel(_dialogService);
                    _launchVm.NotificationRequested += ShowToast;
                    _launchVm.SettingsRequested +=
                        async (versionName) => await OpenSettingsAsync(versionName, nameof(LaunchView));
                    await _launchVm.LoadDataAsync();
                }

                MainContent.Content = new LaunchView { DataContext = _launchVm };
                break;
            }
            case nameof(InstanceView):
            {
                if (_instanceVm == null)
                {
                    _instanceVm = new InstanceViewModel(_dialogService);
                    _instanceVm.SettingsRequested +=
                        async (versionName) => await OpenSettingsAsync(versionName, nameof(InstanceView));
                }

                MainContent.Content = new InstanceView { DataContext = _instanceVm };
                break;
            }
            case nameof(DownloadView):
            {
                MainContent.Content = new DownloadView
                {
                    DataContext = DownloadViewModel.Instance
                };
                break;
            }
            case nameof(MultiplayerView):
            {
                MainContent.Content = new MultiplayerView();
                break;
            }
            case nameof(AccountView):
            {
                _accountVm ??= new AccountViewModel(_dialogService);
                MainContent.Content = new AccountView { DataContext = _accountVm };
                break;
            }
            case nameof(GlobalSettingsView):
            {
                _globalSettingsVm ??= new GlobalSettingsViewModel(_dialogService);
                _globalSettingsVm.LoadCurrent();
                MainContent.Content = new GlobalSettingsView { DataContext = _globalSettingsVm };
                break;
            }
            case nameof(AboutView):
            {
                MainContent.Content = new AboutView();
                break;
            }
            default:
            {
                MainContent.Content = new TextBlock
                {
                    Text = $"页面 {viewName} 不存在",
                    Foreground = Brushes.Red
                };
                break;
            }
        }

        // 一帧后再触发入场动画
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
            Avalonia.Threading.DispatcherPriority.Render);

        try
        {
            MainContent.Opacity = 1;
            MainContent.RenderTransform = PageSettled;
        }
        catch (Exception ex) { LogError(ex); }
    }
}