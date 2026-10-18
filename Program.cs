using Avalonia;
using System;
using System.IO;

namespace NodePulse;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.WriteAllText(logPath,
                    $"=== {DateTime.Now} ==={Environment.NewLine}{ex}");
            }
            catch { }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<AppBoot>()
            .UsePlatformDetect()
            .LogToTrace();
}