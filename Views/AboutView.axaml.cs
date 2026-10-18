using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NodePulse.Views;

public partial class AboutView : UserControl
{
    // ================================================================
    // ★ 配置 4 个链接 / QQ 群号
    // ================================================================

    /// <summary>使用帮助手册（官网帮助页）</summary>
    private const string HelpUrl = "https://www.mcnodepulse.dpdns.org/help.html";

    /// <summary>爱发电赞助地址</summary>
    private const string SponsorUrl = "https://afdian.com/a/Jacky2012";

    /// <summary>GitHub 开源仓库地址</summary>
    private const string GithubUrl = "https://github.com/jacky201208/NodePulse";

    /// <summary>QQ 交流群邀请链接</summary>
    private const string QQGroupUrl = "https://qm.qq.com/q/CGpWgHmSTm";

    // ================================================================

    public AboutView()
    {
        InitializeComponent();
    }

    private void OnOpenHelpClick(object? sender, RoutedEventArgs e)
        => OpenUrl(HelpUrl);

    private void OnOpenSponsorClick(object? sender, RoutedEventArgs e)
        => OpenUrl(SponsorUrl);

    private void OnOpenGithubClick(object? sender, RoutedEventArgs e)
        => OpenUrl(GithubUrl);

    private void OnJoinQQGroupClick(object? sender, RoutedEventArgs e)
        => OpenUrl(QQGroupUrl);

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AboutView] 打开链接失败：{ex.Message}");
        }
    }
}