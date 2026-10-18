using Avalonia.Controls;
using Avalonia.Interactivity;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class MultiplayerView : UserControl
{
    public MultiplayerView()
    {
        InitializeComponent();

        // ★ 绑定到单例 ViewModel —— 切页不丢状态，也不关闭进程
        DataContext = MultiplayerViewModel.Instance;

        // ★ 进入页面时刷新 Linker 就绪状态
        MultiplayerViewModel.Instance.RefreshLinkerReady();
    }

    private async void OnCopyRoomClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MultiplayerViewModel vm) return;
        if (string.IsNullOrEmpty(vm.RoomCode)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(vm.RoomCode);
        }
        catch { }
    }
}