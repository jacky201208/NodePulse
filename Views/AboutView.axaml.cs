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

    /// <summary>QQ 交流群号</summary>
    private const string QQGroupNumber = "386464264";

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

    /// <summary>
    /// 点击直接打开 QQ 网页，调出群聊。
    /// 腾讯官方网页加群链接格式，浏览器会尝试唤起 QQ 客户端并跳转到群聊。
    /// </summary>
    private void OnJoinQQGroupClick(object? sender, RoutedEventArgs e)
    {
        var url = $"https://qun.qq.com/join/{QQGroupNumber}";
        OpenUrl(url);
    }

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