using System;
using Avalonia;
using Avalonia.Styling;
using NodePulse.Models;

namespace NodePulse.Services
{
    /// <summary>
    /// 主题管理器：负责应用 深色 / 浅色 主题。
    /// 主题的实际切换依赖 Application.RequestedThemeVariant，
    /// 配合 Themes/Styles.axaml 里的 ThemeDictionaries 生效。
    /// </summary>
    public static class ThemeManager
    {
        public const string DarkTheme = "dark";
        public const string LightTheme = "light";

        /// <summary>主题变化时触发（参数为新主题名）</summary>
        public static event Action<string>? ThemeChanged;

        /// <summary>当前主题名（"dark" / "light"）</summary>
        public static string CurrentTheme { get; private set; } = DarkTheme;

        /// <summary>应用指定主题</summary>
        public static void Apply(string theme)
        {
            if (string.IsNullOrEmpty(theme))
                theme = DarkTheme;

            theme = theme.ToLowerInvariant();
            if (theme != DarkTheme && theme != LightTheme)
                theme = DarkTheme;

            CurrentTheme = theme;

            if (Application.Current != null)
            {
                Application.Current.RequestedThemeVariant = theme == LightTheme
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
            }

            ThemeChanged?.Invoke(theme);
        }

        /// <summary>从全局设置加载并应用主题</summary>
        public static void LoadFromSettings()
        {
            try
            {
                var settings = GlobalSettings.Load();
                Apply(settings.Theme);
            }
            catch
            {
                Apply(DarkTheme);
            }
        }

        /// <summary>保存到全局设置并应用</summary>
        public static void ApplyAndSave(string theme)
        {
            Apply(theme);

            try
            {
                var settings = GlobalSettings.Load();
                settings.Theme = CurrentTheme;
                settings.Save();
            }
            catch { }
        }

        /// <summary>是否是深色主题</summary>
        public static bool IsDark => CurrentTheme == DarkTheme;

        /// <summary>是否是浅色主题</summary>
        public static bool IsLight => CurrentTheme == LightTheme;
    }
}