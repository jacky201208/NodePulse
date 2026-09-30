using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public SettingsView(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
