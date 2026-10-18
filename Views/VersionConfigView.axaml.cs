using Avalonia.Controls;
using NodePulse.ViewModels;

namespace NodePulse.Views;

public partial class VersionConfigView : UserControl
{
    public VersionConfigView()
    {
        InitializeComponent();
    }

    public VersionConfigView(VersionConfigViewModel vm) : this()
    {
        DataContext = vm;
    }
}