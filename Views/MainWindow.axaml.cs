using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using NodePulse.Services;
using NodePulse.ViewModels;
using NodePulse.Views;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse;

public partial class MainWindow : Window
{
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _toastCts;

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
        catch { }
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
    }

    private void OnFloatingButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        DownloadViewModel.Instance.OpenPanelCommand.Execute(null);
    }

    private async Task SwitchPageAsync(string viewName)
    {
        // 淡出 + 下移
        try
        {
            MainContent.Opacity = 0;
            MainContent.RenderTransform = PageIncoming;
        }
        catch { }

        await Task.Delay(240);

        switch (viewName)
        {
            case nameof(LaunchView):
            {
                var vm = new LaunchViewModel(_dialogService);
                vm.NotificationRequested += ShowToast;

                // ★ 处理"实例设置"事件 → 打开实例设置页
                vm.SettingsRequested += async (versionName) =>
                {
                    var svm = new SettingsViewModel(_dialogService);
                    svm.ReturnRequested += async () =>
                    {
                        await SwitchPageAsync(nameof(LaunchView));
                    };

                    MainContent.Content = new SettingsView { DataContext = svm };
                    await svm.LoadDataAsync();
                    await svm.SelectVersionAsync(versionName);
                };

                MainContent.Content = new LaunchView { DataContext = vm };
                await vm.LoadDataAsync();
                break;
            }
            case nameof(InstanceView):
            {
                var vm = new InstanceViewModel(_dialogService);

                vm.SettingsRequested += async (versionName) =>
                {
                    var svm = new SettingsViewModel(_dialogService);
                    svm.ReturnRequested += async () =>
                    {
                        await SwitchPageAsync(nameof(InstanceView));
                    };

                    MainContent.Content = new SettingsView { DataContext = svm };
                    await svm.LoadDataAsync();
                    await svm.SelectVersionAsync(versionName);
                };

                MainContent.Content = new InstanceView { DataContext = vm };
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
                var vm = new AccountViewModel(_dialogService);
                MainContent.Content = new AccountView { DataContext = vm };
                break;
            }
            case nameof(GlobalSettingsView):
            {
                var vm = new GlobalSettingsViewModel(_dialogService);
                vm.LoadCurrent();
                MainContent.Content = new GlobalSettingsView { DataContext = vm };
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
        catch { }
    }
}