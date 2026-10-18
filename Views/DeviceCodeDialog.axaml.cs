using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;

namespace NodePulse.Views;

public partial class DeviceCodeDialog : Window
{
    public event Action? Cancelled;

    private string _verificationUrl = "";
    private string _userCode = "";

    public DeviceCodeDialog()
    {
        InitializeComponent();

        CopyBtn.Click += OnCopyClick;
        ReopenBtn.Click += OnReopenClick;
        CancelBtn.Click += OnCancelClick;
    }

    public DeviceCodeDialog(string verificationUrl, string userCode) : this()
    {
        _verificationUrl = verificationUrl;
        _userCode = userCode;

        LinkText.Text = verificationUrl;
        CodeText.Text = userCode;

        // 自动复制到剪贴板
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                _ = clipboard.SetTextAsync(userCode);
        }
        catch { }

        // 自动打开浏览器
        OpenBrowser();
    }

    private void OpenBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _verificationUrl,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                _ = clipboard.SetTextAsync(_userCode);
        }
        catch { }
    }

    private void OnReopenClick(object? sender, RoutedEventArgs e) => OpenBrowser();

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Cancelled?.Invoke();
        Close();
    }
}