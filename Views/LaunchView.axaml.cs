using Avalonia.Controls;
using Avalonia.Input;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class LaunchView : UserControl
{
    public LaunchView()
    {
        InitializeComponent();
    }

    public LaunchView(LaunchViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnAccountPickerOverlayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is LaunchViewModel vm)
            vm.CloseAccountPickerCommand.Execute(null);
    }
}