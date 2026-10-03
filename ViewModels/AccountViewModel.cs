using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NodePulse.Helpers;
using NodePulse.Models;
using NodePulse.Services;

namespace NodePulse.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;

        public ObservableCollection<Account> Accounts { get; } = new();

        private readonly Dictionary<string, Bitmap?> _cardAvatarCache = new();

        // ================================================================
        // 卡片翻页
        // ================================================================

        private int _currentIndex;
        public int CurrentIndex
        {
            get => _currentIndex;
            set
            {
                int max = Math.Max(0, Accounts.Count - 1);
                int clamped = Math.Max(0, Math.Min(max, value));
                if (SetProperty(ref _currentIndex, clamped))
                    RefreshCard();
            }
        }

        public string CurrentIndexText =>
            Accounts.Count == 0 ? "0 / 0" : $"{CurrentIndex + 1} / {Accounts.Count}";

        public bool CanGoPrev => CurrentIndex > 0;
        public bool CanGoNext => CurrentIndex < Accounts.Count - 1;
        public bool HasAccounts => Accounts.Count > 0;
        public bool HasNoAccounts => Accounts.Count == 0;

        public Account? CardAccount =>
            CurrentIndex >= 0 && CurrentIndex < Accounts.Count
                ? Accounts[CurrentIndex]
                : null;

        public string CardUsername => CardAccount?.Username ?? "—";

        private Bitmap? _cardAvatarBitmap;
        public Bitmap? CardAvatarBitmap
        {
            get => _cardAvatarBitmap;
            set
            {
                if (SetProperty(ref _cardAvatarBitmap, value))
                {
                    OnPropertyChanged(nameof(CardHasAvatar));
                    OnPropertyChanged(nameof(CardHasNoAvatar));
                }
            }
        }

        public bool CardHasAvatar => _cardAvatarBitmap != null;
        public bool CardHasNoAvatar => _cardAvatarBitmap == null;

        public string CardAvatarInitial
        {
            get
            {
                var n = CardAccount?.Username;
                if (string.IsNullOrWhiteSpace(n)) return "?";
                return n.Trim().Substring(0, 1).ToUpperInvariant();
            }
        }

        public string CardTypeLabel => CardAccount?.TypeLabel ?? "—";

        public string CardUuidText
        {
            get
            {
                var uuid = CardAccount?.Uuid;
                if (string.IsNullOrEmpty(uuid)) return "—";
                return uuid.Length > 24 ? uuid.Substring(0, 24) + "…" : uuid;
            }
        }

        public string CardLastLoginText
        {
            get
            {
                var t = CardAccount?.LastLoginTime;
                return string.IsNullOrEmpty(t) ? "从未登录" : t;
            }
        }

        public bool CardIsYggdrasil => CardAccount?.IsYggdrasil ?? false;

        public bool CardIsActive =>
            CardAccount != null && CurrentAccount != null && CardAccount.Id == CurrentAccount.Id;

        public bool CardIsNotActive => !CardIsActive;

        // ================================================================
        // 详情页
        // ================================================================

        private bool _isDetailView;
        public bool IsDetailView
        {
            get => _isDetailView;
            set => SetProperty(ref _isDetailView, value);
        }

        private Account? _detailAccount;
        public Account? DetailAccount
        {
            get => _detailAccount;
            set
            {
                if (SetProperty(ref _detailAccount, value))
                {
                    OnPropertyChanged(nameof(DetailUsername));
                    OnPropertyChanged(nameof(DetailSubtitle));
                    OnPropertyChanged(nameof(DetailAvatarInitial));
                    OnPropertyChanged(nameof(DetailBadge));
                    OnPropertyChanged(nameof(DetailUuid));
                    OnPropertyChanged(nameof(DetailIsYggdrasil));
                    OnPropertyChanged(nameof(DetailIsCurrent));
                    OnPropertyChanged(nameof(DetailIsNotCurrent));
                    OnPropertyChanged(nameof(DetailHasProfiles));
                    OnPropertyChanged(nameof(DetailSkinNotice));
                    OnPropertyChanged(nameof(HasSkinNotice));
                    OnPropertyChanged(nameof(DetailCanImportSkin));
                    OnPropertyChanged(nameof(DetailManageButtonText));
                    DetailSetCurrentCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string DetailUsername => _detailAccount?.Username ?? "";

        public string DetailSubtitle
        {
            get
            {
                if (_detailAccount == null) return "";

                return _detailAccount.AccountType switch
                {
                    "yggdrasil" => $"第三方账户 · {_detailAccount.ServerName}",
                    "offline" => "离线账户",
                    "microsoft" => "微软账户",
                    _ => _detailAccount.AccountType
                };
            }
        }

        public string DetailAvatarInitial
        {
            get
            {
                var n = _detailAccount?.Username;
                if (string.IsNullOrWhiteSpace(n)) return "?";
                return n.Trim().Substring(0, 1).ToUpperInvariant();
            }
        }

        public string DetailBadge => _detailAccount?.TypeLabel ?? "";

        public string DetailUuid => _detailAccount?.Uuid ?? "";

        public bool DetailIsYggdrasil => _detailAccount?.IsYggdrasil ?? false;

        public bool DetailIsCurrent =>
            _detailAccount != null && CurrentAccount != null &&
            _detailAccount.Id == CurrentAccount.Id;

        public bool DetailIsNotCurrent => !DetailIsCurrent;

        public bool DetailHasProfiles => _detailAccount?.AvailableProfiles?.Count > 0;

        // ★ 是否显示"导入皮肤"按钮（离线 / 微软才显示；外置隐藏）
        public bool DetailCanImportSkin =>
            _detailAccount != null && _detailAccount.AccountType != "yggdrasil";

        // ★ 切换皮肤按钮文字（外置账户提示去皮肤站换）
        public string DetailManageButtonText =>
            DetailIsYggdrasil ? "去皮肤站更换" : "切换皮肤";

        // ================================================================
        // 详情页皮肤
        // ================================================================

        private Bitmap? _avatarBitmap;
        public Bitmap? AvatarBitmap
        {
            get => _avatarBitmap;
            set
            {
                if (SetProperty(ref _avatarBitmap, value))
                {
                    OnPropertyChanged(nameof(HasAvatar));
                    OnPropertyChanged(nameof(HasNoAvatar));
                    OnPropertyChanged(nameof(ShowAvatarError));
                }
            }
        }

        public bool HasAvatar => _avatarBitmap != null;
        public bool HasNoAvatar => _avatarBitmap == null;

        private Bitmap? _capeBitmap;
        public Bitmap? CapeBitmap
        {
            get => _capeBitmap;
            set
            {
                if (SetProperty(ref _capeBitmap, value))
                {
                    OnPropertyChanged(nameof(HasCape));
                    OnPropertyChanged(nameof(HasNoCape));
                }
            }
        }

        public bool HasCape => _capeBitmap != null;
        public bool HasNoCape => _capeBitmap == null;

        private bool _isAvatarLoading;
        public bool IsAvatarLoading
        {
            get => _isAvatarLoading;
            set
            {
                if (SetProperty(ref _isAvatarLoading, value))
                    OnPropertyChanged(nameof(ShowAvatarError));
            }
        }

        public bool ShowAvatarError => !IsAvatarLoading && !HasAvatar;

        private string _avatarErrorMessage = "";
        public string AvatarErrorMessage
        {
            get => _avatarErrorMessage;
            set => SetProperty(ref _avatarErrorMessage, value);
        }

        public string DetailSkinNotice
        {
            get
            {
                if (_detailAccount == null) return "";

                if (!string.IsNullOrEmpty(_detailAccount.LocalSkinPath))
                {
                    if (_detailAccount.LocalSkinPath == "__alex__")
                        return "当前使用默认 Alex 皮肤。";
                    return "当前使用本地导入的皮肤。";
                }

                return _detailAccount.AccountType switch
                {
                    "yggdrasil" =>
                        "皮肤由皮肤站管理。点击「去皮肤站更换」在浏览器中修改，改完点「刷新」即可同步。",
                    "offline" =>
                        "离线账户没有皮肤，显示为默认皮肤。点击「导入皮肤」可上传本地皮肤。",
                    _ => ""
                };
            }
        }

        public bool HasSkinNotice => !string.IsNullOrEmpty(DetailSkinNotice);

        // ================================================================
        // 当前启动账户
        // ================================================================

        private Account? _currentAccount;
        public Account? CurrentAccount
        {
            get => _currentAccount;
            set
            {
                if (SetProperty(ref _currentAccount, value))
                {
                    OnPropertyChanged(nameof(CurrentAccountDisplay));
                    OnPropertyChanged(nameof(HasCurrentAccount));
                    OnPropertyChanged(nameof(CardIsActive));
                    OnPropertyChanged(nameof(CardIsNotActive));
                    OnPropertyChanged(nameof(DetailIsCurrent));
                    OnPropertyChanged(nameof(DetailIsNotCurrent));
                }
            }
        }

        public string CurrentAccountDisplay =>
            CurrentAccount?.DisplayText ?? "未选择账户";

        public bool HasCurrentAccount => CurrentAccount != null;

        // ================================================================
        // 状态
        // ================================================================

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // ================================================================
        // Commands
        // ================================================================

        public AsyncRelayCommand AddOfflineCommand { get; }
        public AsyncRelayCommand AddYggdrasilCommand { get; }
        public AsyncRelayCommand AddMicrosoftCommand { get; }
        public AsyncRelayCommand RemoveCurrentCommand { get; }
        public RelayCommand SetCurrentCommand { get; }
        public AsyncRelayCommand ChangeProfileCommand { get; }
        public RelayCommand PrevCommand { get; }
        public RelayCommand NextCommand { get; }

        public RelayCommand<Account> OpenDetailCommand { get; }
        public RelayCommand CloseDetailCommand { get; }
        public AsyncRelayCommand RefreshSkinCommand { get; }
        public RelayCommand DetailSetCurrentCommand { get; }
        public AsyncRelayCommand RemoveDetailCommand { get; }
        public AsyncRelayCommand ChangeDetailProfileCommand { get; }

        public AsyncRelayCommand ImportSkinCommand { get; }
        public AsyncRelayCommand ManageSkinsCommand { get; }

        public AccountViewModel(IDialogService dialogService)
        {
            _dialogService = dialogService;

            AddOfflineCommand = new AsyncRelayCommand(AddOfflineAsync);
            AddYggdrasilCommand = new AsyncRelayCommand(AddYggdrasilAsync);
            AddMicrosoftCommand = new AsyncRelayCommand(AddMicrosoftAsync);
            RemoveCurrentCommand = new AsyncRelayCommand(RemoveCurrentAsync);
            SetCurrentCommand = new RelayCommand(SetCurrentFromCard);
            ChangeProfileCommand = new AsyncRelayCommand(ChangeProfileAsync);

            PrevCommand = new RelayCommand(
                () => CurrentIndex--,
                () => CanGoPrev);

            NextCommand = new RelayCommand(
                () => CurrentIndex++,
                () => CanGoNext);

            OpenDetailCommand = new RelayCommand<Account>(OpenDetail);
            CloseDetailCommand = new RelayCommand(CloseDetail);
            RefreshSkinCommand = new AsyncRelayCommand(LoadAvatarAsync);
            DetailSetCurrentCommand = new RelayCommand(DetailSetCurrent, () => DetailIsNotCurrent);
            RemoveDetailCommand = new AsyncRelayCommand(RemoveDetailAsync);
            ChangeDetailProfileCommand = new AsyncRelayCommand(ChangeDetailProfileAsync);

            ImportSkinCommand = new AsyncRelayCommand(ImportSkinAsync);
            ManageSkinsCommand = new AsyncRelayCommand(ManageSkinsAsync);

            LoadAccounts();
        }

        // ================================================================
        // 载入
        // ================================================================

        private void LoadAccounts()
        {
            var store = AccountService.Load();
            Accounts.Clear();
            foreach (var a in store.Accounts)
                Accounts.Add(a);

            CurrentAccount = Accounts.FirstOrDefault(a => a.Id == store.CurrentAccountId)
                            ?? Accounts.FirstOrDefault();

            _currentIndex = CurrentAccount != null
                ? Math.Max(0, Accounts.IndexOf(CurrentAccount))
                : 0;

            RefreshAll();

            StatusText = Accounts.Count == 0
                ? "还没有账户，点击下方按钮添加"
                : $"共 {Accounts.Count} 个账户";
        }

        private void Persist()
        {
            AccountService.Save(new AccountStore
            {
                Accounts = Accounts.ToList(),
                CurrentAccountId = CurrentAccount?.Id ?? ""
            });
        }

        private void RefreshCard()
        {
            OnPropertyChanged(nameof(CurrentIndexText));
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CardAccount));
            OnPropertyChanged(nameof(CardUsername));
            OnPropertyChanged(nameof(CardAvatarInitial));
            OnPropertyChanged(nameof(CardTypeLabel));
            OnPropertyChanged(nameof(CardUuidText));
            OnPropertyChanged(nameof(CardLastLoginText));
            OnPropertyChanged(nameof(CardIsYggdrasil));
            OnPropertyChanged(nameof(CardIsActive));
            OnPropertyChanged(nameof(CardIsNotActive));

            PrevCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();

            _ = LoadCardAvatarAsync(CardAccount);
        }

        private void RefreshAll()
        {
            OnPropertyChanged(nameof(CurrentIndex));
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(HasNoAccounts));
            RefreshCard();
        }

        // ================================================================
        // ★ 皮肤导入 / 管理
        // ================================================================

        private async Task ImportSkinAsync()
        {
            var acc = DetailAccount;
            if (acc == null) return;

            // 外置账户不允许导入
            if (acc.AccountType == "yggdrasil")
            {
                await _dialogService.ShowMessageAsync("提示",
                    "第三方账户的皮肤由皮肤站统一管理，请前往皮肤站更换。");
                return;
            }

            var file = await _dialogService.PickFileAsync(
                "选择皮肤文件（64×64 PNG）",
                new[] { "*.png" });

            if (string.IsNullOrEmpty(file)) return;

            try
            {
                int w, h;
                try
                {
                    using var testBmp = new Bitmap(file);
                    w = testBmp.PixelSize.Width;
                    h = testBmp.PixelSize.Height;
                }
                catch
                {
                    await _dialogService.ShowMessageAsync("提示",
                        "无法读取该文件，请选择有效的 PNG 皮肤文件。");
                    return;
                }

                if (w != h || w < 64)
                {
                    await _dialogService.ShowMessageAsync("提示",
                        "请选择有效的 Minecraft 皮肤文件（正方形 PNG，边长 ≥ 64）。");
                    return;
                }

                var skinsDir = Path.Combine(AppContext.BaseDirectory, ".NodePulse", "skins");
                Directory.CreateDirectory(skinsDir);

                string targetPath;

                var fullSource = Path.GetFullPath(file);
                if (fullSource.StartsWith(Path.GetFullPath(skinsDir),
                        StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = fullSource;
                }
                else
                {
                    targetPath = Path.Combine(skinsDir, $"{Guid.NewGuid():N}.png");
                    await Task.Run(() => File.Copy(file, targetPath, overwrite: true));
                }

                acc.LocalSkinPath = targetPath;
                Persist();

                _cardAvatarCache.Remove(acc.Id);
                _ = LoadAvatarAsync();

                OnPropertyChanged(nameof(DetailSkinNotice));
                OnPropertyChanged(nameof(HasSkinNotice));

                StatusText = $"已导入皮肤：{Path.GetFileName(file)}";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync("导入失败", ex.Message);
            }
        }

        private async Task ManageSkinsAsync()
        {
            var acc = DetailAccount;
            if (acc == null) return;

            // ★ 外置账户：打开皮肤站官网
            if (acc.AccountType == "yggdrasil" && !string.IsNullOrEmpty(acc.ApiRoot))
            {
                var siteUrl = GetSiteBaseUrl(acc.ApiRoot);

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = siteUrl,
                            UseShellExecute = true
                        });
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        Process.Start("open", siteUrl);
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        Process.Start("xdg-open", siteUrl);
                    }

                    StatusText = $"已在浏览器打开 {acc.ServerName}，去那换皮肤吧";
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowMessageAsync("提示",
                        $"无法打开浏览器：{ex.Message}\n\n请手动访问：{siteUrl}");
                }

                return;
            }

            // 离线 / 微软：打开本地皮肤管理窗口
            var selected = await _dialogService.ShowSkinManagerAsync(acc);
            if (selected == null) return;

            acc.LocalSkinPath = selected;
            Persist();

            _cardAvatarCache.Remove(acc.Id);
            _ = LoadAvatarAsync();

            OnPropertyChanged(nameof(DetailSkinNotice));
            OnPropertyChanged(nameof(HasSkinNotice));

            StatusText = "已切换皮肤";
        }

        /// <summary>
        /// 从 ApiRoot 提取皮肤站官网地址
        ///   https://littleskin.cn/api/yggdrasil → https://littleskin.cn
        ///   https://littleskin.cn                → https://littleskin.cn
        /// </summary>
        private static string GetSiteBaseUrl(string apiRoot)
        {
            var root = apiRoot.Trim().TrimEnd('/');

            var idx = root.IndexOf("/api/yggdrasil", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return root.Substring(0, idx);

            return root;
        }

        // ================================================================
        // 卡片头像加载
        // ================================================================

        private async Task LoadCardAvatarAsync(Account? acc)
        {
            if (acc == null)
            {
                CardAvatarBitmap = null;
                return;
            }

            if (_cardAvatarCache.TryGetValue(acc.Id, out var cached))
            {
                CardAvatarBitmap = cached;
                return;
            }

            CardAvatarBitmap = null;

            var bmp = await LoadSkinInternalAsync(acc, isCard: true);

            if (bmp != null)
                _cardAvatarCache[acc.Id] = bmp;

            if (ReferenceEquals(CardAccount, acc))
            {
                await Dispatcher.UIThread.InvokeAsync(() => CardAvatarBitmap = bmp);
            }
        }

        // ================================================================
        // 核心：加载皮肤 Bitmap
        // ================================================================

        private static async Task<Bitmap?> LoadSkinInternalAsync(Account acc, bool isCard)
        {
            try
            {
                // 1. 本地皮肤
                if (!string.IsNullOrEmpty(acc.LocalSkinPath))
                {
                    if (acc.LocalSkinPath == "__alex__")
                    {
                        return await LoadFromMcHeadsAsync("MHF_Alex", isCard);
                    }

                    if (File.Exists(acc.LocalSkinPath))
                    {
                        var bytes = await File.ReadAllBytesAsync(acc.LocalSkinPath);
                        if (bytes.Length > 100)
                        {
                            return isCard
                                ? CropHeadFromSkin(bytes)
                                : BuildBodyFromSkin(bytes);
                        }
                    }
                }

                // 2. 外置账户：从皮肤站
                if (acc.AccountType == "yggdrasil" && !string.IsNullOrEmpty(acc.ApiRoot))
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
                            {
                                var result = isCard
                                    ? CropHeadFromSkin(bytes)
                                    : BuildBodyFromSkin(bytes);
                                if (result != null) return result;
                            }
                        }
                        catch { }
                    }
                }

                // 3. 兜底
                return await LoadFromMcHeadsAsync(acc.Username, isCard);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<Bitmap?> LoadFromMcHeadsAsync(string username, bool isCard)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                if (isCard)
                {
                    var url = $"https://mc-heads.net/avatar/" +
                              $"{Uri.EscapeDataString(username)}/128";
                    var bytes = await http.GetByteArrayAsync(url);
                    if (bytes.Length < 100) return null;

                    using var ms = new MemoryStream(bytes);
                    return new Bitmap(ms);
                }
                else
                {
                    var url = $"https://mc-heads.net/skin/{Uri.EscapeDataString(username)}";
                    var bytes = await http.GetByteArrayAsync(url);
                    if (bytes.Length < 100) return null;
                    return BuildBodyFromSkin(bytes);
                }
            }
            catch
            {
                return null;
            }
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

        // ================================================================
        // 详情页操作
        // ================================================================

        private void OpenDetail(Account? acc)
        {
            if (acc == null) return;

            DetailAccount = acc;
            IsDetailView = true;
            _ = LoadAvatarAsync();
        }

        private void CloseDetail()
        {
            IsDetailView = false;
            AvatarBitmap = null;
            CapeBitmap = null;
            AvatarErrorMessage = "";
            DetailAccount = null;
        }

        private void DetailSetCurrent()
        {
            var acc = DetailAccount;
            if (acc == null) return;

            CurrentAccount = acc;
            Persist();

            OnPropertyChanged(nameof(DetailIsCurrent));
            OnPropertyChanged(nameof(DetailIsNotCurrent));
            DetailSetCurrentCommand.RaiseCanExecuteChanged();

            StatusText = $"已设为当前账户：{acc.DisplayText}";
        }

        private async Task ChangeDetailProfileAsync()
        {
            var acc = DetailAccount;
            if (acc == null || !acc.IsYggdrasil) return;

            if (acc.AvailableProfiles.Count == 0)
            {
                await _dialogService.ShowMessageAsync("提示",
                    "该账户没有缓存的角色列表，请重新登录");
                return;
            }

            var chosen = await _dialogService.ShowProfileSelectAsync(
                "切换角色", acc.AvailableProfiles);

            if (chosen == null) return;

            acc.Username = chosen.Name;
            acc.Uuid = chosen.Id;
            acc.LastLoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var idx = Accounts.IndexOf(acc);
            if (idx >= 0)
                Accounts[idx] = acc;

            _cardAvatarCache.Remove(acc.Id);

            Persist();
            RefreshAll();

            OnPropertyChanged(nameof(DetailUsername));
            OnPropertyChanged(nameof(DetailSubtitle));
            OnPropertyChanged(nameof(DetailAvatarInitial));
            OnPropertyChanged(nameof(DetailUuid));
            OnPropertyChanged(nameof(DetailSkinNotice));
            OnPropertyChanged(nameof(HasSkinNotice));

            _ = LoadAvatarAsync();

            StatusText = $"已切换角色：{acc.Username}";
        }

        private async Task RemoveDetailAsync()
        {
            var acc = DetailAccount;
            if (acc == null) return;

            var confirmed = await _dialogService.ShowConfirmAsync(
                "删除账户",
                $"确定要删除账户「{acc.Username}」吗？\n\n此操作不可恢复。");
            if (!confirmed) return;

            int idx = Accounts.IndexOf(acc);
            Accounts.Remove(acc);
            _cardAvatarCache.Remove(acc.Id);

            if (CurrentAccount == acc)
                CurrentAccount = Accounts.FirstOrDefault();

            if (Accounts.Count == 0)
                _currentIndex = 0;
            else if (idx >= Accounts.Count)
                _currentIndex = Accounts.Count - 1;
            else if (idx >= 0)
                _currentIndex = idx;

            Persist();
            RefreshAll();
            CloseDetail();

            StatusText = $"已删除：{acc.Username}";
        }

        // ================================================================
        // 详情页皮肤 + 披风加载
        // ================================================================

        private async Task LoadAvatarAsync()
        {
            var acc = _detailAccount;
            if (acc == null)
            {
                AvatarBitmap = null;
                CapeBitmap = null;
                return;
            }

            IsAvatarLoading = true;
            AvatarBitmap = null;
            CapeBitmap = null;
            AvatarErrorMessage = "";

            try
            {
                var skin = await LoadSkinInternalAsync(acc, isCard: false);

                Bitmap? cape = null;
                if (acc.AccountType == "yggdrasil" &&
                    string.IsNullOrEmpty(acc.LocalSkinPath) &&
                    !string.IsNullOrEmpty(acc.ApiRoot))
                {
                    try
                    {
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                        http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                        var capeUrl = await FetchYggdrasilCapeUrlAsync(http, acc);
                        if (!string.IsNullOrEmpty(capeUrl))
                        {
                            var bytes = await http.GetByteArrayAsync(capeUrl);
                            if (bytes.Length > 100)
                                cape = BuildCapeFromTexture(bytes);
                        }
                    }
                    catch { }
                }

                if (skin == null)
                {
                    skin = await LoadFromMcHeadsAsync("MHF_Steve", isCard: false);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AvatarBitmap = skin;
                    CapeBitmap = cape;
                    IsAvatarLoading = false;
                    AvatarErrorMessage = skin == null ? "皮肤加载失败" : "";
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AvatarBitmap = null;
                    CapeBitmap = null;
                    IsAvatarLoading = false;
                    AvatarErrorMessage = $"加载失败：{ex.Message}";
                });
            }
        }

        private static async Task<string?> FetchYggdrasilCapeUrlAsync(
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
                    var capeUrl = texRoot?["textures"]?["CAPE"]?["url"]?.GetValue<string>();
                    return capeUrl;
                }
            }
            catch { }

            return null;
        }

        // ================================================================
        // 规范化 ApiRoot
        // ================================================================

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

        // ================================================================
        // 拼图工具
        // ================================================================

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

        private static Bitmap? BuildBodyFromSkin(byte[] skinBytes)
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

                const int outUnit = 16;
                const int outW = 16 * outUnit;
                const int outH = 32 * outUnit;

                var output = new RenderTargetBitmap(new PixelSize(outW, outH));

                using (var ctx = output.CreateDrawingContext(false))
                {
                    DrawPart(ctx, src, scale, 8, 8, 8, 8, 4, 0, 8, 8, outUnit);
                    DrawPart(ctx, src, scale, 44, 20, 4, 12, 0, 8, 4, 12, outUnit);
                    DrawPart(ctx, src, scale, 20, 20, 8, 12, 4, 8, 8, 12, outUnit);
                    DrawPart(ctx, src, scale, 36, 52, 4, 12, 12, 8, 4, 12, outUnit);
                    DrawPart(ctx, src, scale, 4, 20, 4, 12, 4, 20, 4, 12, outUnit);
                    DrawPart(ctx, src, scale, 20, 52, 4, 12, 8, 20, 4, 12, outUnit);
                }

                return output;
            }
            catch { return null; }
        }

        private static void DrawPart(DrawingContext ctx, Bitmap src, int scale,
            int sx, int sy, int sw, int sh,
            int dx, int dy, int dw, int dh, int outUnit)
        {
            var srcRect = new Rect(sx * scale, sy * scale, sw * scale, sh * scale);
            var dstRect = new Rect(dx * outUnit, dy * outUnit, dw * outUnit, dh * outUnit);

            using (ctx.PushRenderOptions(new RenderOptions
            {
                BitmapInterpolationMode = BitmapInterpolationMode.None
            }))
            {
                ctx.DrawImage(src, srcRect, dstRect);
            }
        }

        private static Bitmap? BuildCapeFromTexture(byte[] capeBytes)
        {
            try
            {
                using var ms = new MemoryStream(capeBytes);
                using var src = new Bitmap(ms);

                if (src.PixelSize.Width < 64 || src.PixelSize.Height < 32)
                    return null;

                const int outUnit = 16;
                const int outW = 10 * outUnit;
                const int outH = 16 * outUnit;

                var output = new RenderTargetBitmap(new PixelSize(outW, outH));

                using (var ctx = output.CreateDrawingContext(false))
                {
                    var srcRect = new Rect(12, 1, 10, 16);
                    var dstRect = new Rect(0, 0, outW, outH);

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
        // 添加离线账户
        // ================================================================

        private async Task AddOfflineAsync()
        {
            var name = await _dialogService.ShowTextInputAsync(
                "添加离线账户", "请输入用户名（游戏内显示的名字）", "Player");

            if (string.IsNullOrWhiteSpace(name)) return;

            if (Accounts.Any(a => a.AccountType == "offline" &&
                                  a.Username.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                await _dialogService.ShowMessageAsync("提示", $"已存在同名离线账户：{name}");
                return;
            }

            var acc = OfflineAuth.Create(name);
            Accounts.Add(acc);

            if (CurrentAccount == null)
                CurrentAccount = acc;

            _currentIndex = Accounts.Count - 1;

            Persist();
            RefreshAll();

            StatusText = $"已添加离线账户：{acc.Username}";
        }

        // ================================================================
        // 添加外置登录
        // ================================================================

        private async Task AddYggdrasilAsync()
        {
            var input = await _dialogService.ShowYggdrasilLoginAsync();
            if (input == null) return;

            string apiRoot, username, password;
            if (_dialogService is AvaloniaDialogService avalonia)
            {
                apiRoot = avalonia.GetPendingApiRoot();
                username = avalonia.GetPendingUsername();
                password = avalonia.GetPendingPassword();
            }
            else
            {
                await _dialogService.ShowMessageAsync("错误", "当前对话框服务不支持此操作");
                return;
            }

            if (string.IsNullOrWhiteSpace(apiRoot) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                await _dialogService.ShowMessageAsync("提示", "请填写完整信息");
                return;
            }

            StatusText = "正在登录外置账户...";

            var (result, error) = await YggdrasilAuth.LoginAsync(apiRoot, username, password);

            if (result == null)
            {
                StatusText = "外置登录失败";
                await _dialogService.ShowMessageAsync("登录失败", error ?? "未知错误");
                return;
            }

            ProfileInfo? chosen;
            if (result.AvailableProfiles.Count == 1)
            {
                chosen = result.AvailableProfiles[0];
            }
            else
            {
                chosen = await _dialogService.ShowProfileSelectAsync(
                    "选择角色", result.AvailableProfiles);

                if (chosen == null)
                {
                    StatusText = "已取消";
                    return;
                }
            }

            var acc = new Account
            {
                Username = chosen.Name,
                Uuid = chosen.Id,
                AccessToken = result.AccessToken,
                ClientToken = result.ClientToken,
                UserType = "mojang",
                AccountType = "yggdrasil",
                ApiRoot = apiRoot,
                ServerName = YggdrasilAuth.GuessServerName(apiRoot),
                AvailableProfiles = result.AvailableProfiles,
                LastLoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var existing = Accounts.FirstOrDefault(a =>
                a.AccountType == "yggdrasil" &&
                a.ApiRoot.Equals(acc.ApiRoot, StringComparison.OrdinalIgnoreCase) &&
                a.Uuid == acc.Uuid);

            if (existing != null)
            {
                int idx = Accounts.IndexOf(existing);
                Accounts[idx] = acc;
                _currentIndex = idx;
                _cardAvatarCache.Remove(acc.Id);
            }
            else
            {
                Accounts.Add(acc);
                _currentIndex = Accounts.Count - 1;
            }

            if (CurrentAccount == null || CurrentAccount.AccountType != "yggdrasil")
                CurrentAccount = acc;

            Persist();
            RefreshAll();

            StatusText = $"外置登录成功：{acc.Username} @ {acc.ServerName}";
        }

        // ================================================================
        // 添加正版（微软）账户
        // ================================================================

        private async Task AddMicrosoftAsync()
        {
            StatusText = "正在打开微软授权...";

            var login = await _dialogService.ShowMicrosoftLoginAsync();

            if (login == null)
            {
                StatusText = "已取消微软登录";
                return;
            }

            var acc = new Account
            {
                Username = login.Username,
                Uuid = FormatUuid(login.Uuid),
                AccessToken = login.AccessToken,
                ClientToken = MicrosoftAuth.ClientId,
                UserType = "msa",
                AccountType = "microsoft",
                ServerName = "Microsoft",
                LastLoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            var existing = Accounts.FirstOrDefault(a =>
                a.AccountType == "microsoft" &&
                a.Uuid == acc.Uuid);

            if (existing != null)
            {
                int idx = Accounts.IndexOf(existing);
                Accounts[idx] = acc;
                _currentIndex = idx;
                _cardAvatarCache.Remove(acc.Id);
            }
            else
            {
                Accounts.Add(acc);
                _currentIndex = Accounts.Count - 1;
            }

            if (CurrentAccount == null || CurrentAccount.AccountType != "microsoft")
                CurrentAccount = acc;

            Persist();
            RefreshAll();

            StatusText = $"正版登录成功：{acc.Username}";
        }

        /// <summary>把无连字符 UUID 还原为带连字符形式。</summary>
        private static string FormatUuid(string raw)
        {
            raw = raw?.Trim() ?? "";
            if (raw.Length == 32 && !raw.Contains('-'))
            {
                return raw.Substring(0, 8) + "-" +
                       raw.Substring(8, 4) + "-" +
                       raw.Substring(12, 4) + "-" +
                       raw.Substring(16, 4) + "-" +
                       raw.Substring(20, 12);
            }
            return raw;
        }

        // ================================================================
        // 卡片底部操作
        // ================================================================

        private async Task ChangeProfileAsync()
        {
            var acc = CardAccount;
            if (acc == null || !acc.IsYggdrasil) return;

            if (acc.AvailableProfiles.Count == 0)
            {
                await _dialogService.ShowMessageAsync("提示",
                    "该账户没有缓存的角色列表，请重新登录");
                return;
            }

            var chosen = await _dialogService.ShowProfileSelectAsync(
                "切换角色", acc.AvailableProfiles);

            if (chosen == null) return;

            acc.Username = chosen.Name;
            acc.Uuid = chosen.Id;
            acc.LastLoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var idx = Accounts.IndexOf(acc);
            if (idx >= 0)
                Accounts[idx] = acc;

            _cardAvatarCache.Remove(acc.Id);

            Persist();
            RefreshAll();

            StatusText = $"已切换角色：{acc.Username}";
        }

        private async Task RemoveCurrentAsync()
        {
            var acc = CardAccount;
            if (acc == null) return;

            var confirmed = await _dialogService.ShowConfirmAsync(
                "删除账户",
                $"确定要删除账户「{acc.Username}」吗？\n\n此操作不可恢复。");
            if (!confirmed) return;

            int idx = CurrentIndex;
            Accounts.Remove(acc);
            _cardAvatarCache.Remove(acc.Id);

            if (CurrentAccount == acc)
                CurrentAccount = Accounts.FirstOrDefault();

            if (Accounts.Count == 0)
                _currentIndex = 0;
            else if (idx >= Accounts.Count)
                _currentIndex = Accounts.Count - 1;

            Persist();
            RefreshAll();

            StatusText = $"已删除：{acc.Username}";
        }

        private void SetCurrentFromCard()
        {
            var acc = CardAccount;
            if (acc == null) return;

            CurrentAccount = acc;
            Persist();

            OnPropertyChanged(nameof(CardIsActive));
            OnPropertyChanged(nameof(CardIsNotActive));

            StatusText = $"已设为当前账户：{acc.DisplayText}";
        }
    }
}