using System;
using System.Windows.Input;
using NodePulse.Helpers;
using NodePulse.Services;
using NodePulse.Views;

namespace NodePulse.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        /// <summary>页面切换请求，由 MainWindow 订阅后执行实际跳转。</summary>
        public event Action<string>? NavigationRequested;

        /// <summary>导航命令。</summary>
        public ICommand GoLaunchPageCommand { get; }
        public ICommand GoDownloadPageCommand { get; }
        public ICommand GoInstancePageCommand { get; }
        public ICommand GoSettingsPageCommand { get; }
        public ICommand GoGlobalSettingsPageCommand { get; }
        public ICommand GoAccountPageCommand { get; }
        public ICommand GoMultiplayerPageCommand { get; }
        public ICommand GoAboutPageCommand { get; }

        public MainWindowViewModel(IDialogService dialogService)
        {
            GoLaunchPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(LaunchView)));
            GoDownloadPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(DownloadView)));
            GoInstancePageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(InstanceView)));
            GoSettingsPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(SettingsView)));
            GoGlobalSettingsPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(GlobalSettingsView)));
            GoAccountPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(AccountView)));
            GoMultiplayerPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(MultiplayerView)));
            GoAboutPageCommand = new RelayCommand(() => NavigationRequested?.Invoke(nameof(AboutView)));
        }
    }
}