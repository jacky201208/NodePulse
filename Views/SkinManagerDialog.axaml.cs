using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NodePulse.Models;

namespace NodePulse.Views;

public partial class SkinItem : ObservableObject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsBuiltIn { get; set; }

    [ObservableProperty] private Bitmap? _preview;
}

public partial class SkinManagerDialog : Window
{
    /// <summary>
    /// null        → 取消
    /// ""          → 默认 Steve
    /// "__alex__"  → 默认 Alex
    /// 绝对路径    → 本地皮肤
    /// </summary>
    public string? SelectedSkinPath { get; private set; }

    private readonly Account _account;

    public SkinManagerDialog()
    {
        InitializeComponent();
        _account = new Account();
        Loaded += (_, _) => _ = LoadAllAsync();
    }

    public SkinManagerDialog(Account account) : this()
    {
        // 因为无参构造已经执行了 InitializeComponent，
        // 直接覆盖 account 引用即可
        // 下面用反射 field 赋值不可行（readonly），改成在 LoadAll 时读取
        _accountOverride = account;
    }

    private Account? _accountOverride;

    private Account EffectiveAccount => _accountOverride ?? _account;

    private async Task LoadAllAsync()
    {
        var builtIn = new ObservableCollection<SkinItem>
        {
            new SkinItem { Name = "默认 Steve", Path = "", IsBuiltIn = true },
            new SkinItem { Name = "默认 Alex", Path = "__alex__", IsBuiltIn = true },
        };

        var imported = new List<SkinItem>();

        var skinsDir = Path.Combine(AppContext.BaseDirectory, ".NodePulse", "skins");
        if (Directory.Exists(skinsDir))
        {
            foreach (var file in Directory.GetFiles(skinsDir, "*.png")
                                           .OrderByDescending(File.GetLastWriteTime))
            {
                imported.Add(new SkinItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Path = file,
                    IsBuiltIn = false
                });
            }
        }

        BuiltInList.ItemsSource = builtIn;
        ImportedList.ItemsSource = imported;

        EmptyHint.IsVisible = imported.Count == 0;

        // 标注当前使用的皮肤
        var currentPath = EffectiveAccount.LocalSkinPath ?? "";
        string statusText;
        if (string.IsNullOrEmpty(currentPath))
            statusText = "当前使用：默认 Steve";
        else if (currentPath == "__alex__")
            statusText = "当前使用：默认 Alex";
        else
            statusText = $"当前使用：{Path.GetFileNameWithoutExtension(currentPath)}";
        StatusLabel.Text = statusText;

        // 异步加载预览
        foreach (var item in builtIn) _ = LoadPreviewAsync(item);
        foreach (var item in imported) _ = LoadPreviewAsync(item);
    }

    private static async Task LoadPreviewAsync(SkinItem item)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

            if (item.IsBuiltIn)
            {
                var name = item.Path == "__alex__" ? "MHF_Alex" : "MHF_Steve";
                var url = $"https://mc-heads.net/avatar/{name}/128";
                var bytes = await http.GetByteArrayAsync(url);
                if (bytes.Length > 100)
                {
                    using var ms = new MemoryStream(bytes);
                    item.Preview = new Bitmap(ms);
                }
                return;
            }

            if (File.Exists(item.Path))
            {
                var bytes = await File.ReadAllBytesAsync(item.Path);
                var bmp = CropHeadFromSkin(bytes);
                if (bmp != null)
                    item.Preview = bmp;
            }
        }
        catch { }
    }

    /// <summary>从 64×64 皮肤纹理裁出头像（8,8,8,8）</summary>
    private static Bitmap? CropHeadFromSkin(byte[] skinBytes)
    {
        try
        {
            using var ms = new MemoryStream(skinBytes);
            using var src = new Bitmap(ms);

            int w = src.PixelSize.Width;
            int h = src.PixelSize.Height;
            if (w != h || w < 64) return null;

            int scale = w / 64;
            if (scale < 1) scale = 1;

            const int outSize = 128;
            var output = new RenderTargetBitmap(new PixelSize(outSize, outSize));

            using (var ctx = output.CreateDrawingContext(false))
            {
                var srcRect = new Rect(8 * scale, 8 * scale, 8 * scale, 8 * scale);
                var dstRect = new Rect(0, 0, outSize, outSize);

                using (ctx.PushRenderOptions(new RenderOptions
                {
                    BitmapInterpolationMode = BitmapInterpolationMode.None
                }))
                {
                    ctx.DrawImage(src, srcRect, dstRect);
                }
            }

            return output;
        }
        catch
        {
            return null;
        }
    }

    private void OnSkinClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not SkinItem item) return;

        SelectedSkinPath = item.Path;
        Close();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        SelectedSkinPath = null;
        Close();
    }
}