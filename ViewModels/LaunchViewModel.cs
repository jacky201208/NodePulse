using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    /// <summary>
    /// 实例选择列表里的一项
    /// </summary>
    public partial class LaunchInstanceItem : ObservableObject
    {
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string LoaderType { get; set; } = "vanilla";

        [ObservableProperty] private Bitmap? _iconBitmap;
        [ObservableProperty] private bool _isCurrent;

        public double IconSize => LoaderType switch
        {
            "vanilla" => 36,
            "optifine" => 44,
            "quilt" => 44,
            "forge" => 38,
            _ => 40
        };
    }

    public class LaunchViewModel : INotifyPropertyChanged
    {
        private readonly IDialogService _dialogService;

        public event Action<string>? NotificationRequested;
        public event Action<string>? SettingsRequested;

        private List<string> _installedVersions = new();
        public List<string> InstalledVersions
        {
            get => _installedVersions;
            set { _installedVersions = value; OnPropertyChanged(); }
        }

        private string? _selectedVersion;
        public string? SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                _selectedVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedVersion));
                LaunchCommand.RaiseCanExecuteChanged();
                SaveLastSelectedVersion();

                if (!string.IsNullOrEmpty(value))
                    _ = LoadSelectedVersionInfoAsync(value);
            }
        }

        // ================================================================
        // 选中版本信息
        // ================================================================

        private Bitmap? _selectedVersionIcon;
        public Bitmap? SelectedVersionIcon
        {
            get => _selectedVersionIcon;
            set { _selectedVersionIcon = value; OnPropertyChanged(); }
        }

        private string _selectedVersionSubtitle = "";
        public string SelectedVersionSubtitle
        {
            get => _selectedVersionSubtitle;
            set { _selectedVersionSubtitle = value; OnPropertyChanged(); }
        }

        public bool HasSelectedVersion => !string.IsNullOrEmpty(_selectedVersion);

        // ================================================================
        // 实例选择界面
        // ================================================================

        public ObservableCollection<LaunchInstanceItem> InstanceItems { get; } = new();

        private bool _isPickingInstance;
        public bool IsPickingInstance
        {
            get => _isPickingInstance;
            set { _isPickingInstance = value; OnPropertyChanged(); }
        }

        private bool _isPickerLoading;
        public bool IsPickerLoading
        {
            get => _isPickerLoading;
            set { _isPickerLoading = value; OnPropertyChanged(); }
        }

        public RelayCommand OpenInstanceSettingsCommand { get; }
        public RelayCommand OpenInstancePickerCommand { get; }
        public RelayCommand CloseInstancePickerCommand { get; }
        public RelayCommand<LaunchInstanceItem> PickInstanceCommand { get; }

        // ================================================================
        // 账户信息 + 头像
        // ================================================================

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _currentAccountText = "未选择账户";
        public string CurrentAccountText
        {
            get => _currentAccountText;
            set { _currentAccountText = value; OnPropertyChanged(); }
        }

        // ================================================================
        // 一键切换账号
        // ================================================================

        public ObservableCollection<Account> AllAccounts { get; } = new();

        private bool _isAccountPickerOpen;
        public bool IsAccountPickerOpen
        {
            get => _isAccountPickerOpen;
            set { _isAccountPickerOpen = value; OnPropertyChanged(); }
        }

        public RelayCommand OpenAccountPickerCommand { get; }
        public RelayCommand<Account> SwitchAccountCommand { get; }

        // 首字母（加载失败时的兜底）
        private string _accountAvatarInitial = "?";
        public string AccountAvatarInitial
        {
            get => _accountAvatarInitial;
            set { _accountAvatarInitial = value; OnPropertyChanged(); }
        }

        // ★ 真实头像
        private Bitmap? _accountAvatarBitmap;
        public Bitmap? AccountAvatarBitmap
        {
            get => _accountAvatarBitmap;
            set
            {
                _accountAvatarBitmap = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasAccountAvatar));
                OnPropertyChanged(nameof(HasNoAccountAvatar));
            }
        }

        public bool HasAccountAvatar => _accountAvatarBitmap != null;
        public bool HasNoAccountAvatar => _accountAvatarBitmap == null;

        // ================================================================

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotLoading));
                LaunchCommand.RaiseCanExecuteChanged();
            }
        }

        private double _launchProgress;
        public double LaunchProgress
        {
            get => _launchProgress;
            set { _launchProgress = value; OnPropertyChanged(); }
        }

        private string _progressDetail = "";
        public string ProgressDetail
        {
            get => _progressDetail;
            set { _progressDetail = value; OnPropertyChanged(); }
        }

        public bool IsNotLoading => !IsLoading;
        public AsyncRelayCommand LaunchCommand { get; }

        public LaunchViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            LaunchCommand = new AsyncRelayCommand(
                DoLaunch,
                () => !IsLoading && !string.IsNullOrWhiteSpace(SelectedVersion));

            OpenInstanceSettingsCommand = new RelayCommand(OpenInstanceSettings);
            OpenInstancePickerCommand = new RelayCommand(() => _ = OpenInstancePickerAsync());
            CloseInstancePickerCommand = new RelayCommand(CloseInstancePicker);
            PickInstanceCommand = new RelayCommand<LaunchInstanceItem>(PickInstance);
            OpenAccountPickerCommand = new RelayCommand(OpenAccountPicker);
            SwitchAccountCommand = new RelayCommand<Account>(SwitchAccount);
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            StatusText = "正在扫描已安装版本...";

            await Task.Run(() =>
            {
                InstalledVersions = VersionScanner.GetInstalledVersions();
            });

            var g = GlobalSettings.Load();
            if (!string.IsNullOrEmpty(g.LastVersion) && InstalledVersions.Contains(g.LastVersion))
            {
                SelectedVersion = g.LastVersion;
            }
            else if (InstalledVersions.Count > 0)
            {
                SelectedVersion = InstalledVersions[0];
            }

            LoadAccounts();

            var account = AccountService.GetCurrent();
            CurrentAccountText = account?.DisplayText ?? "未选择账户（请在【账户】页面添加）";
            AccountAvatarInitial = GetInitial(account?.Username);

            // ★ 异步加载真实头像
            if (account != null)
                _ = LoadAccountAvatarAsync(account);

            StatusText = InstalledVersions.Count == 0
                ? "没有已安装的游戏版本"
                : $"共 {InstalledVersions.Count} 个已安装版本";
            IsLoading = false;
        }

        // ================================================================
// 一键切换账号
// ================================================================

        private void LoadAccounts()
        {
            AllAccounts.Clear();
            foreach (var a in AccountService.GetAll())
                AllAccounts.Add(a);
        }

        private void OpenAccountPicker()
        {
            LoadAccounts();
            IsAccountPickerOpen = !IsAccountPickerOpen;
        }

        private void SwitchAccount(Account? acc)
        {
            if (acc == null) return;

            if (!AccountService.SetCurrent(acc.Id))
                return;

            IsAccountPickerOpen = false;
            LoadAccounts();

            CurrentAccountText = acc.DisplayText;
            AccountAvatarInitial = GetInitial(acc.Username);

            // 切换后立即刷新头像
            AccountAvatarBitmap = null;
            _ = LoadAccountAvatarAsync(acc);

            StatusText = $"已切换到账号：{acc.Username}";
        }

        private static string GetInitial(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var trimmed = name.Trim();
            return trimmed.Length > 0
                ? trimmed.Substring(0, 1).ToUpperInvariant()
                : "?";
        }

        // ================================================================
        // ★ 加载账户头像
        // ================================================================

        private async Task LoadAccountAvatarAsync(Account acc)
        {
            try
            {
                Bitmap? bmp = null;

                // 1. 本地皮肤（离线 / 微软）
                if (!string.IsNullOrEmpty(acc.LocalSkinPath))
                {
                    if (acc.LocalSkinPath == "__alex__")
                    {
                        bmp = await LoadAvatarFromMcHeadsAsync("MHF_Alex");
                    }
                    else if (File.Exists(acc.LocalSkinPath))
                    {
                        var bytes = await File.ReadAllBytesAsync(acc.LocalSkinPath);
                        if (bytes.Length > 100)
                            bmp = CropHeadFromSkin(bytes);
                    }
                }

                // 2. 外置账户：皮肤站
                if (bmp == null && acc.AccountType == "yggdrasil" &&
                    !string.IsNullOrEmpty(acc.ApiRoot))
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                    var skinUrl = await FetchYggdrasilSkinUrlAsync(http, acc);
                    if (!string.IsNullOrEmpty(skinUrl))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(skinUrl);
                            if (bytes.Length > 100)
                                bmp = CropHeadFromSkin(bytes);
                        }
                        catch { }
                    }
                }

                // 3. 兜底：mc-heads.net
                if (bmp == null)
                    bmp = await LoadAvatarFromMcHeadsAsync(acc.Username);

                // 4. Steve 兜底
                if (bmp == null)
                    bmp = await LoadAvatarFromMcHeadsAsync("MHF_Steve");

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AccountAvatarBitmap = bmp;
                });
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AccountAvatarBitmap = null;
                });
            }
        }

        private static async Task<Bitmap?> LoadAvatarFromMcHeadsAsync(string username)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                var url = $"https://mc-heads.net/avatar/" +
                          $"{Uri.EscapeDataString(username)}/256";

                var bytes = await http.GetByteArrayAsync(url);
                if (bytes.Length < 100) return null;

                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch { return null; }
        }

        private static async Task<string?> FetchYggdrasilSkinUrlAsync(
            HttpClient http, Account account)
        {
            try
            {
                var apiRoot = NormalizeApiRoot(account.ApiRoot);
                var uuidNoDash = account.Uuid.Replace("-", "").ToLowerInvariant();
                var url = $"{apiRoot}/sessionserver/session/minecraft/profile/{uuidNoDash}?unsigned=false";

                var json = await http.GetStringAsync(url);
                var node = JsonNode.Parse(json)?.AsObject();
                var props = node?["properties"]?.AsArray();
                if (props == null) return null;

                foreach (var prop in props)
                {
                    var name = prop?["name"]?.GetValue<string>();
                    if (name != "textures") continue;

                    var value = prop?["value"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(value)) continue;

                    var decoded = Encoding.UTF8.GetString(
                        Convert.FromBase64String(value));
                    var texRoot = JsonNode.Parse(decoded)?.AsObject();
                    var skinUrl = texRoot?["textures"]?["SKIN"]?["url"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(skinUrl))
                        return skinUrl;
                }
            }
            catch { }

            return null;
        }

        private static string NormalizeApiRoot(string raw)
        {
            var root = raw.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(root)) return root;

            if (root.EndsWith("/api/yggdrasil", StringComparison.OrdinalIgnoreCase))
                return root;

            var idx = root.IndexOf("/api/yggdrasil", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return root.Substring(0, idx + "/api/yggdrasil".Length);

            return root + "/api/yggdrasil";
        }

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

                const int outSize = 256;
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
            catch { return null; }
        }

        // ================================================================
        // 选中版本信息加载
        // ================================================================

        private async Task LoadSelectedVersionInfoAsync(string versionName)
        {
            try
            {
                var inst = await Task.Run(() =>
                    MinecraftInstance.ScanAll()
                        .FirstOrDefault(i => i.Name == versionName));

                if (inst == null) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try { inst.LoadIcon(); } catch { }

                    SelectedVersionIcon = inst.IconBitmap;

                    var loader = inst.LoaderType switch
                    {
                        "fabric" => "Fabric",
                        "forge" => "Forge",
                        "neoforge" => "NeoForge",
                        "quilt" => "Quilt",
                        "optifine" => "OptiFine",
                        _ => "原版"
                    };

                    SelectedVersionSubtitle =
                        $"{loader}  ·  Java {inst.DisplayJavaMajor}";
                });
            }
            catch { }
        }

        private void SaveLastSelectedVersion()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion)) return;
            var g = GlobalSettings.Load();
            g.LastVersion = SelectedVersion;
            g.Save();
        }

        // ================================================================
        // 实例设置 / 选择
        // ================================================================

        private void OpenInstanceSettings()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion)) return;
            SettingsRequested?.Invoke(SelectedVersion);
        }

        private async Task OpenInstancePickerAsync()
        {
            IsPickingInstance = true;
            IsPickerLoading = true;
            InstanceItems.Clear();

            try
            {
                var instances = await Task.Run(() => MinecraftInstance.ScanAll());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var inst in instances)
                    {
                        try { inst.LoadIcon(); } catch { }

                        var loader = inst.LoaderType switch
                        {
                            "fabric" => "Fabric",
                            "forge" => "Forge",
                            "neoforge" => "NeoForge",
                            "quilt" => "Quilt",
                            "optifine" => "OptiFine",
                            _ => "原版"
                        };

                        InstanceItems.Add(new LaunchInstanceItem
                        {
                            Name = inst.Name,
                            Subtitle = $"{loader}  ·  Java {inst.DisplayJavaMajor}  ·  {FormatSize(inst.TotalSize)}",
                            LoaderType = inst.LoaderType,
                            IconBitmap = inst.IconBitmap,
                            IsCurrent = string.Equals(inst.Name, SelectedVersion,
                                StringComparison.OrdinalIgnoreCase)
                        });
                    }
                });
            }
            catch { }

            IsPickerLoading = false;
        }

        private void CloseInstancePicker()
        {
            IsPickingInstance = false;
            InstanceItems.Clear();
        }

        private void PickInstance(LaunchInstanceItem? item)
        {
            if (item == null) return;

            SelectedVersion = item.Name;
            CloseInstancePicker();
            StatusText = $"已选择：{item.Name}";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }

        // ================================================================
        // 启动
        // ================================================================

        private async Task DoLaunch()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion)) return;

            var account = AccountService.GetCurrent();
            if (account == null)
            {
                await _dialogService.ShowMessageAsync("提示",
                    "还没有账户，请先到【账户】页面添加一个离线账户。");
                return;
            }

            var ver = SelectedVersion;

            IsLoading = true;
            LaunchProgress = 0;
            ProgressDetail = "";
            StatusText = "正在校验游戏文件...";

            try
            {
                var validateProgress = new Progress<DownloadProgress>(p =>
                {
                    if (p.Total > 0)
                    {
                        LaunchProgress = p.Percent * 0.9;
                        ProgressDetail = $"{p.CurrentFile}  ({p.Done}/{p.Total})";
                    }
                    else
                    {
                        LaunchProgress = 5;
                        ProgressDetail = p.CurrentFile;
                    }
                });

                await Task.Run(async () =>
                {
                    await MinecraftLauncher.ValidateAndRepairAsync(ver, validateProgress);
                });

                LaunchProgress = 92;
                ProgressDetail = "文件校验完成";
                StatusText = "正在启动游戏...";
                await Task.Delay(300);

                Process? proc = null;
                Exception? launchEx = null;

                await Task.Run(() =>
                {
                    try
                    {
                        proc = MinecraftLauncher.Launch(ver);
                    }
                    catch (Exception ex)
                    {
                        launchEx = ex;
                    }
                });

                if (launchEx != null || proc == null)
                {
                    LaunchProgress = 0;
                    ProgressDetail = "";
                    IsLoading = false;
                    StatusText = "启动失败";
                    await _dialogService.ShowMessageAsync("错误",
                        $"启动失败：{launchEx?.Message ?? "未知错误"}");
                    return;
                }

                LaunchProgress = 99;
                ProgressDetail = "等待游戏窗口出现...";
                StatusText = "等待游戏窗口出现...";

                bool windowAppeared = await WaitForGameWindowAsync(proc);

                if (windowAppeared)
                {
                    LaunchProgress = 100;
                    ProgressDetail = "游戏已启动";
                    await Task.Delay(500);

                    IsLoading = false;
                    ProgressDetail = "";
                    StatusText = $"{ver} 已启动";
                    NotificationRequested?.Invoke($"「{ver}」已启动！");
                }
                else
                {
                    IsLoading = false;
                    ProgressDetail = "";
                    StatusText = "游戏进程已退出，请检查 mc-latest.log";
                    await _dialogService.ShowMessageAsync("提示",
                        "游戏进程已退出，可能启动失败。\n\n" +
                        "请查看程序目录下的 mc-latest.log 获取详细信息。");
                }
            }
            catch (Exception ex)
            {
                LaunchProgress = 0;
                ProgressDetail = "";
                IsLoading = false;
                StatusText = "启动失败";
                await _dialogService.ShowMessageAsync("错误",
                    $"启动失败：{ex.Message}");
            }
        }

        private static async Task<bool> WaitForGameWindowAsync(Process proc)
        {
            var sw = Stopwatch.StartNew();
            var timeout = TimeSpan.FromMinutes(5);

            while (sw.Elapsed < timeout)
            {
                try
                {
                    if (proc.HasExited)
                        return false;
                }
                catch
                {
                    return false;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        proc.Refresh();
                        if (proc.MainWindowHandle != IntPtr.Zero)
                            return true;
                    }
                    catch { }
                }
                else
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(15))
                        return true;
                }

                await Task.Delay(500);
            }

            return false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}