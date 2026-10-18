using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class DownloadView : UserControl
{
    private bool _loadTriggered;
    private DownloadViewModel? _hookedVm;

    /// <summary>已创建的 Tab 视图缓存（LRU，最多保留 2 个）</summary>
    private readonly Dictionary<DownloadTabKind, Control> _viewCache = new();

    /// <summary>LRU 顺序（队首最旧）</summary>
    private readonly List<DownloadTabKind> _cacheOrder = new();

    /// <summary>★ 视图缓存上限：超过则释放最旧的视图</summary>
    private const int MaxCachedViews = 1;

    public DownloadView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_loadTriggered) return;
        _loadTriggered = true;

        if (DataContext is DownloadViewModel vm)
        {
            vm.EnsureLoaded();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_hookedVm != null)
        {
            _hookedVm.PropertyChanged -= OnVmPropertyChanged;
            _hookedVm = null;
        }

        if (DataContext is not DownloadViewModel vm) return;

        _hookedVm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadViewModel.SelectedTab))
            UpdateTabContent();
    }

    private void UpdateTabContent()
    {
        if (DataContext is not DownloadViewModel vm) return;

        if (vm.SelectedTab == DownloadTabKind.Instance)
        {
            TabContentHost.Content = null;
            return;
        }

        // 缓存命中 → 更新 LRU 顺序，直接复用
        if (_viewCache.TryGetValue(vm.SelectedTab, out var cached))
        {
            _cacheOrder.Remove(vm.SelectedTab);
            _cacheOrder.Add(vm.SelectedTab);
            TabContentHost.Content = cached;
            return;
        }

        Control? view = null;

        switch (vm.SelectedTab)
        {
            case DownloadTabKind.Mod:
                view = new ContentDownloadView { DataContext = vm.ModVm };
                break;
            case DownloadTabKind.Modpack:
                view = new ContentDownloadView { DataContext = vm.ModpackVm };
                break;
            case DownloadTabKind.DataPack:
                view = new ContentDownloadView { DataContext = vm.DatapackVm };
                break;
            case DownloadTabKind.ShaderPack:
                view = new ContentDownloadView { DataContext = vm.ShaderVm };
                break;
            case DownloadTabKind.CustomDownload:
                view = new CustomDownloadView { DataContext = vm.CustomVm };
                break;
        }

        if (view == null) return;

        // ★ LRU 淘汰：超过上限时释放最旧的视图
        if (_viewCache.Count >= MaxCachedViews && _cacheOrder.Count > 0)
        {
            var oldest = _cacheOrder[0];
            _cacheOrder.RemoveAt(0);
            _viewCache.Remove(oldest);
        }

        _viewCache[vm.SelectedTab] = view;
        _cacheOrder.Add(vm.SelectedTab);
        TabContentHost.Content = view;
    }
}