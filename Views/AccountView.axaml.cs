using Avalonia.Controls;
using Avalonia.Input;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class AccountView : UserControl
{
    public AccountView()
    {
        InitializeComponent();
    }

    public AccountView(AccountViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// 卡片被点击 → 打开详情页
    /// </summary>
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not AccountViewModel vm) return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;

        var acc = vm.CardAccount;
        if (acc == null) return;

        vm.OpenDetailCommand.Execute(acc);
        e.Handled = true;
    }
}