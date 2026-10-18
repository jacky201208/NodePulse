using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class ContentDownloadView : UserControl
{
    private bool _initialLoadDone;
    private ContentDownloadViewModel? _hookedVm;

    public ContentDownloadView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnViewLoaded;
    }

    private async void OnViewLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await EnsureInitialLoadAsync();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_hookedVm != null)
        {
            _hookedVm.ResultsUpdated -= OnVmResultsUpdated;
            _hookedVm = null;
        }

        if (DataContext is not ContentDownloadViewModel vm) return;

        _hookedVm = vm;
        vm.ResultsUpdated += OnVmResultsUpdated;
    }

    private async Task EnsureInitialLoadAsync()
    {
        if (_initialLoadDone) return;
        if (DataContext is not ContentDownloadViewModel vm) return;

        _initialLoadDone = true;

        try
        {
            await vm.LoadInstancesAsync();

            if (vm.SearchResults.Count == 0)
                await vm.LoadPopularAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ContentDownloadView] 初始加载失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 每次翻页数据到达后，把结果区滚回顶部。
    /// </summary>
    private void OnVmResultsUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try { ResultScroll.Offset = new Avalonia.Vector(0, 0); }
            catch { }
        }, DispatcherPriority.Background);
    }
}