using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using NodePulse.Services;

namespace NodePulse;

public class AppBoot : Application
{
    public override void Initialize()
    {
        // ★ 加载自定义颜色资源（含 Dark / Light 两套）
        try
        {
            var uri = new Uri("avares://NodePulse/Themes/Styles.axaml");
            var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
            Resources.MergedDictionaries.Add(dict);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AppBoot] 加载 Themes/Styles.axaml 失败: {ex.Message}");
        }

        // 加载 Fluent 主题（提供原生控件的基础样式）
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

        // ★ 全局统一按钮样式（蓝→青渐变，与"启动游戏"按钮一致）
        try
        {
            var gUri = new Uri("avares://NodePulse/Themes/GlobalStyles.axaml");
            var gStyles = (Styles)AvaloniaXamlLoader.Load(gUri);
            Styles.Add(gStyles);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AppBoot] 加载 Themes/GlobalStyles.axaml 失败: {ex.Message}");
        }

        // ★ 应用用户设置的主题
        ThemeManager.LoadFromSettings();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}