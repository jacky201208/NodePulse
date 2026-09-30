using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class InstanceView : UserControl
{
    public InstanceView()
    {
        InitializeComponent();
    }

    public InstanceView(InstanceViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
