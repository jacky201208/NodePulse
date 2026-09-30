using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NodePulse.Models;
using NodePulse.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NodePulse.Services
{
    public class AvaloniaDialogService : IDialogService
    {
        private readonly Window _mainWindow;

        public AvaloniaDialogService(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public async Task ShowMessageAsync(string title, string message)
        {
            var dialog = BuildDialog(title, 420, 200);

            var okBtn = new Button
            {
                Content = "确定",
                Width = 80,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            okBtn.Click += (_, _) => dialog.Close();

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    },
                    okBtn
                }
            };

            await dialog.ShowDialog(_mainWindow);
        }

        public async Task<bool> ShowConfirmAsync(string title, string message)
        {
            bool result = false;
            var dialog = BuildDialog(title, 420, 200);

            var yesBtn = new Button { Content = "确定", Width = 80, Height = 32 };
            var noBtn = new Button { Content = "取消", Width = 80, Height = 32 };
            yesBtn.Click += (_, _) => { result = true; dialog.Close(); };
            noBtn.Click += (_, _) => { result = false; dialog.Close(); };

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        Foreground = Brushes.White,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { noBtn, yesBtn }
                    }
                }
            };

            await dialog.ShowDialog(_mainWindow);
            return result;
        }

        public async Task<string?> PickFolderAsync(string title)
        {
            var top = TopLevel.GetTopLevel(_mainWindow);
            if (top == null) return null;

            var folders = await top.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }

        public async Task<string?> PickFileAsync(string title, string[] patterns)
        {
            var top = TopLevel.GetTopLevel(_mainWindow);
            if (top == null) return null;

            var types = patterns.Select(p =>
                new FilePickerFileType(p) { Patterns = new[] { p } }).ToList();

            var files = await top.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = types
                });

            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }

        public async Task<string?> ShowTextInputAsync(string title, string label, string defaultValue = "")
        {
            string? result = null;
            var dialog = BuildDialog(title, 400, 200);

            var box = new TextBox
            {
                Text = defaultValue,
                Height = 34,
                Background = new SolidColorBrush(Color.Parse("#121120")),
                Foreground = new SolidColorBrush(Color.Parse("#E2E2F2"))
            };

            var okBtn = new Button { Content = "确定", Width = 80, Height = 32 };
            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 32 };

            okBtn.Click += (_, _) =>
            {
                result = box.Text?.Trim() ?? "";
                dialog.Close();
            };
            cancelBtn.Click += (_, _) => dialog.Close();

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = label, Foreground = Brushes.White },
                    box,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, okBtn }
                    }
                }
            };

            await dialog.ShowDialog(_mainWindow);
            return result;
        }

        public async Task<YggdrasilLoginResult?> ShowYggdrasilLoginAsync()
        {
            YggdrasilLoginResult? result = null;
            var dialog = BuildDialog("外置登录", 480, 440);

            var apiRootBox = new TextBox
            {
                Text = YggdrasilAuth.LittleSkinApi,
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#121120")),
                Foreground = new SolidColorBrush(Color.Parse("#E2E2F2"))
            };

            var littleSkinBtn = new Button { Content = "LittleSkin", Height = 26, Padding = new Avalonia.Thickness(10, 0) };
            var elyByBtn = new Button { Content = "Ely.by", Height = 26, Padding = new Avalonia.Thickness(10, 0) };
            littleSkinBtn.Click += (_, _) => apiRootBox.Text = YggdrasilAuth.LittleSkinApi;
            elyByBtn.Click += (_, _) => apiRootBox.Text = YggdrasilAuth.ElyByApi;

            var userBox = new TextBox
            {
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#121120")),
                Foreground = new SolidColorBrush(Color.Parse("#E2E2F2"))
            };

            var passBox = new TextBox
            {
                PasswordChar = '●',
                Height = 32,
                Background = new SolidColorBrush(Color.Parse("#121120")),
                Foreground = new SolidColorBrush(Color.Parse("#E2E2F2"))
            };

            var okBtn = new Button { Content = "登录", Width = 90, Height = 34 };
            var cancelBtn = new Button { Content = "取消", Width = 90, Height = 34 };

            okBtn.Click += (_, _) =>
            {
                result = new YggdrasilLoginResult
                {
                    AccessToken = "",
                    ClientToken = ""
                };
                _pendingApiRoot = apiRootBox.Text?.Trim() ?? "";
                _pendingUsername = userBox.Text?.Trim() ?? "";
                _pendingPassword = passBox.Text ?? "";
                dialog.Close();
            };
            cancelBtn.Click += (_, _) => dialog.Close();

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "皮肤站 API Root", Foreground = Brushes.White },
                    apiRootBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { littleSkinBtn, elyByBtn }
                    },
                    new TextBlock
                    {
                        Text = "留空输入框时可直接点下方预设按钮",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#8888A0"))
                    },
                    new TextBlock { Text = "用户名 / 邮箱", Foreground = Brushes.White, Margin = new Avalonia.Thickness(0, 8, 0, 0) },
                    userBox,
                    new TextBlock { Text = "密码", Foreground = Brushes.White, Margin = new Avalonia.Thickness(0, 8, 0, 0) },
                    passBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new Avalonia.Thickness(0, 16, 0, 0),
                        Children = { cancelBtn, okBtn }
                    }
                }
            };

            await dialog.ShowDialog(_mainWindow);
            return result;
        }

        private string _pendingApiRoot = "";
        private string _pendingUsername = "";
        private string _pendingPassword = "";

        public string GetPendingApiRoot() => _pendingApiRoot;
        public string GetPendingUsername() => _pendingUsername;
        public string GetPendingPassword() => _pendingPassword;

        public async Task<ProfileInfo?> ShowProfileSelectAsync(string title, List<ProfileInfo> profiles)
        {
            ProfileInfo? selected = profiles.Count > 0 ? profiles[0] : null;
            var dialog = BuildDialog(title, 400, 400);

            var listBox = new ListBox
            {
                ItemsSource = profiles,
                SelectedIndex = 0,
                Height = 240,
                Background = new SolidColorBrush(Color.Parse("#121120")),
                Foreground = new SolidColorBrush(Color.Parse("#E2E2F2"))
            };

            listBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ProfileInfo>(
                (p, _) =>
                {
                    var tb = new TextBlock
                    {
                        Text = p?.Name ?? "",
                        Foreground = new SolidColorBrush(Color.Parse("#E2E2F2")),
                        Padding = new Avalonia.Thickness(8, 6)
                    };
                    return tb;
                });

            listBox.SelectionChanged += (_, _) =>
            {
                if (listBox.SelectedItem is ProfileInfo p)
                    selected = p;
            };

            var okBtn = new Button { Content = "确定", Width = 80, Height = 32 };
            var cancelBtn = new Button { Content = "取消", Width = 80, Height = 32 };

            okBtn.Click += (_, _) => dialog.Close();
            cancelBtn.Click += (_, _) =>
            {
                selected = null;
                dialog.Close();
            };

            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "请选择要使用的角色：",
                        Foreground = Brushes.White
                    },
                    listBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelBtn, okBtn }
                    }
                }
            };

            await dialog.ShowDialog(_mainWindow);
            return selected;
        }

        public async Task<string?> ShowSaveFileAsync(
            string title,
            string suggestedFileName,
            string defaultExtension = "jar",
            string? startDirectory = null)
        {
            var top = TopLevel.GetTopLevel(_mainWindow);
            if (top == null) return null;

            IStorageFolder? startFolder = null;

            if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
            {
                try
                {
                    startFolder = await top.StorageProvider.TryGetFolderFromPathAsync(
                        new Uri(startDirectory));
                }
                catch { }
            }

            var fileType = new FilePickerFileType(defaultExtension.ToUpperInvariant())
            {
                Patterns = new[] { $"*.{defaultExtension}" }
            };

            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                FileTypeChoices = new[] { fileType },
                ShowOverwritePrompt = true
            };

            if (startFolder != null)
                options.SuggestedStartLocation = startFolder;

            var file = await top.StorageProvider.SaveFilePickerAsync(options);

            return file?.Path.LocalPath;
        }

        // ================================================================
        // ★ 皮肤管理对话框
        // ================================================================

        public async Task<string?> ShowSkinManagerAsync(Account account)
        {
            try
            {
                var dialog = new SkinManagerDialog(account);
                await dialog.ShowDialog(_mainWindow);
                return dialog.SelectedSkinPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AvaloniaDialogService] 皮肤管理窗口失败: {ex.Message}");
                return null;
            }
        }

        private static Window BuildDialog(string title, double width, double height)
        {
            return new Window
            {
                Title = title,
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#1E1C30"))
            };
        }
    }
}