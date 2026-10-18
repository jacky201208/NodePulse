using Avalonia.Controls;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class GlobalSettingsView : UserControl
{
    public GlobalSettingsView()
    {
        InitializeComponent();
    }

    public GlobalSettingsView(GlobalSettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}