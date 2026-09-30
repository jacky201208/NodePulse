using Avalonia.Controls;
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
}