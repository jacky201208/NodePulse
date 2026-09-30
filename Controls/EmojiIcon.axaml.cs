using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using NodePulse.Services;

namespace NodePulse.Controls
{
    /// <summary>
    /// Emoji / 图标显示控件：
    ///   浅色模式 → 显示原图
    ///   深色模式 → 用原图 alpha 通道渲染成白色剪影
    /// </summary>
    public partial class EmojiIcon : UserControl
    {
        public static readonly StyledProperty<string?> IconSourceProperty =
            AvaloniaProperty.Register<EmojiIcon, string?>(nameof(IconSource));

        public string? IconSource
        {
            get => GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }

        public static readonly StyledProperty<double> IconSizeProperty =
            AvaloniaProperty.Register<EmojiIcon, double>(nameof(IconSize), 20.0);

        public double IconSize
        {
            get => GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        private Bitmap? _bitmap;

        public EmojiIcon()
        {
            InitializeComponent();

            ThemeManager.ThemeChanged += OnThemeChanged;

            AttachedToVisualTree += (_, _) =>
            {
                ReloadBitmap();
                ApplyTheme();
            };
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IconSourceProperty)
            {
                ReloadBitmap();
                ApplyTheme();
            }
            else if (change.Property == IconSizeProperty)
            {
                ApplyTheme();
            }
        }

        private void OnThemeChanged(string _)
        {
            if (Dispatcher.UIThread.CheckAccess())
                ApplyTheme();
            else
                Dispatcher.UIThread.Post(ApplyTheme);
        }

        private void ReloadBitmap()
        {
            _bitmap = null;

            var uri = IconSource;
            if (string.IsNullOrEmpty(uri)) return;

            try
            {
                var u = new Uri(uri);
                if (!AssetLoader.Exists(u)) return;

                using var stream = AssetLoader.Open(u);
                _bitmap = new Bitmap(stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EmojiIcon] 加载失败 {uri}: {ex.Message}");
            }
        }

        private void ApplyTheme()
        {
            double size = IconSize;
            PART_Light.Width = size;
            PART_Light.Height = size;
            PART_Dark.Width = size;
            PART_Dark.Height = size;

            if (_bitmap == null)
            {
                PART_Light.IsVisible = false;
                PART_Dark.IsVisible = false;
                return;
            }

            // 浅色模式：直接显示原图
            PART_Light.Source = _bitmap;

            // 深色模式：用原图当 alpha 遮罩，填充白色
            PART_Dark.OpacityMask = new ImageBrush(_bitmap)
            {
                Stretch = Stretch.Uniform
            };

            bool isDark = ThemeManager.IsDark;
            PART_Light.IsVisible = !isDark;
            PART_Dark.IsVisible = isDark;
        }

        protected override void OnDetachedFromVisualTree(
            Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _bitmap = null;
        }
    }
}