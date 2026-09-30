using Avalonia.Controls;
using Avalonia.Interactivity;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class AccountDetailView : UserControl
{
    public AccountDetailView()
    {
        InitializeComponent();
    }

    private async void OnCopyUuidClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountViewModel vm) return;
        if (string.IsNullOrEmpty(vm.DetailUuid)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(vm.DetailUuid);
        }
        catch { }
    }
}