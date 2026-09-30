using System;
using System.Windows.Input;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        private object? _currentView;
        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public ICommand GoLaunchPageCommand { get; }
        public ICommand GoDownloadPageCommand { get; }
        public ICommand GoInstancePageCommand { get; }
        public ICommand GoSettingsPageCommand { get; }
        public ICommand GoGlobalSettingsPageCommand { get; }
        public ICommand GoAccountPageCommand { get; }

        public MainWindowViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            GoLaunchPageCommand = new RelayCommand(() => { });
            GoDownloadPageCommand = new RelayCommand(() => { });
            GoInstancePageCommand = new RelayCommand(() => { });
            GoSettingsPageCommand = new RelayCommand(() => { });
            GoGlobalSettingsPageCommand = new RelayCommand(() => { });
            GoAccountPageCommand = new RelayCommand(() => { });
        }
    }

    // 简易内置 RelayCommand
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
            => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter)
            => _execute();

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}