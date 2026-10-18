using Avalonia.Controls;
using Avalonia.Input;
using NodePulse.Models;
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

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border
            && border.DataContext is Account acc
            && DataContext is AccountViewModel vm)
        {
            vm.OpenDetailCommand.Execute(acc);
        }
    }
}