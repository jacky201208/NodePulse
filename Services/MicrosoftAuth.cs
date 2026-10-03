using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

namespace NodePulse.Services
{
    /// <summary>微软正版账户 OAuth 设备码登录。</summary>
    public static partial class MicrosoftAuth
    {
        /// <summary>
        /// 微软正版登录所需的 Azure 应用 Client ID。
        /// 出于安全考虑，源码不内置任何正式 Client ID。
        /// 使用方需通过环境变量 NODEPULSE_AZURE_CLIENT_ID 提供自己的
        /// Azure 应用（已获得 Minecraft/Xbox 相关权限）Client ID。
        /// </summary>
        public static string ClientId =>
            Environment.GetEnvironmentVariable("NODEPULSE_AZURE_CLIENT_ID") ?? "";

        private const string MsaAuthorizeBase =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0";
        private const string XboxAuthenticate =
            "https://user.auth.xboxlive.com/user/authenticate";
        private const string XstsAuthorize =
            "https://xsts.auth.xboxlive.com/xsts/authorize";
        private const string MinecraftLogin =
            "https://api.minecraftservices.com/authentication/login_with_xbox";
        private const string MinecraftProfile =
            "https://api.minecraftservices.com/minecraft/profile";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        /// <summary>请求设备码授权信息。</summary>
        public static async Task<(DeviceCodeInfo? result, string? error)> RequestDeviceCodeAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ClientId))
                    return (null, "未配置微软登录 Client ID，请设置环境变量 NODEPULSE_AZURE_CLIENT_ID");

                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "client_id", ClientId },
                    { "scope", "XboxLive.signin offline_access" }
                });

                var url = $"{MsaAuthorizeBase}/devicecode";
                var resp = await _http.PostAsync(url, form);
                var text = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    var desc = TryGetNode(text)?["error_description"]?.GetValue<string>();
                    return (null, desc ?? $"获取设备码失败（HTTP {(int)resp.StatusCode}）");
                }

                var node = JsonNode.Parse(text);
                return (new DeviceCodeInfo
                {
                    DeviceCode = node!["device_code"]?.GetValue<string>() ?? "",
                    UserCode = node["user_code"]?.GetValue<string>() ?? "",
                    VerificationUri = node["verification_uri"]?.GetValue<string>() ?? "",
                    ExpiresIn = node["expires_in"]?.GetValue<int>() ?? 900,
                    Interval = node["interval"]?.GetValue<int>() ?? 5
                }, null);
            }
            catch (TaskCanceledException)
            {
                return (null, "请求超时，请检查网络");
            }
            catch (HttpRequestException ex)
            {
                return (null, $"网络错误：{ex.Message}");
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// 轮询授权结果直到用户完成授权，成功后继续完成 Xbox / Minecraft 登录。
        /// 返回 cancelled=true 表示用户主动取消或授权过期。
        /// </summary>
        public static async Task<(MicrosoftLoginResult? result, string? error, bool cancelled)>
            PollAndLoginAsync(DeviceCodeInfo device, CancellationToken ct)
        {
            MicrosoftAccessToken? msaToken = null;
            var started = DateTime.UtcNow;

            while (ct.IsCancellationRequested == false)
            {
                if ((DateTime.UtcNow - started).TotalSeconds > device.ExpiresIn)
                    return (null, "授权已过期，请重新登录", true);

                try
                {
                    var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "client_id", ClientId },
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                        { "device_code", device.DeviceCode }
                    });

                    var url = $"{MsaAuthorizeBase}/token";
                    var resp = await _http.PostAsync(url, form, ct);
                    var text = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        var node = TryGetNode(text);
                        var err = node?["error"]?.GetValue<string>();

                        if (err == "authorization_pending")
                        {
                            await Task.Delay(device.Interval * 1000, ct);
                            continue;
                        }

                        if (err == "authorization_declined")
                            return (null, "用户拒绝了授权", true);

                        if (err == "expired_token")
                            return (null, "授权已过期，请重新登录", true);

                        var desc = node?["error_description"]?.GetValue<string>();
                        return (null, desc ?? $"登录失败（HTTP {(int)resp.StatusCode}）", true);
                    }

                    var okNode = JsonNode.Parse(text);
                    msaToken = new MicrosoftAccessToken
                    {
                        AccessToken = okNode!["access_token"]?.GetValue<string>() ?? "",
                        RefreshToken = okNode["refresh_token"]?.GetValue<string>() ?? ""
                    };
                    break;
                }
                catch (TaskCanceledException)
                {
                    return (null, null, true);
                }
                catch (HttpRequestException ex)
                {
                    return (null, $"网络错误：{ex.Message}", true);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message, true);
                }
            }

            if (ct.IsCancellationRequested || msaToken == null)
                return (null, null, true);

            return await CompleteLoginAsync(msaToken.AccessToken);
        }

        /// <summary>用微软令牌换取 Minecraft 访问令牌与角色信息。</summary>
        private static async Task<(MicrosoftLoginResult? result, string? error, bool cancelled)>
            CompleteLoginAsync(string msaAccessToken)
        {
            try
            {
                // 1. 换取 Xbox Live 令牌（保留 uhs）
                var xbl = await DoPostJsonAsync<XboxAuthResponse>
                    (XboxAuthenticate, new Dictionary<string, object>
                    {
                        { "Properties", new Dictionary<string, object>
                            {
                                { "AuthMethod", "RPS" },
                                { "SiteName", "user.auth.xboxlive.com" },
                                { "RpsTicket", "d=" + msaAccessToken }
                            } },
                        { "RelyingParty", "http://auth.xboxlive.com" },
                        { "TokenType", "JWT" }
                    }, msaAccessToken, needBearer: false);

                string uhs = xbl?.DisplayClaims?.Xui?[0]?.Uhs ?? "";
                if (string.IsNullOrEmpty(uhs))
                    return (null, "无法解析 Xbox 身份标识", true);

                // 2. 换取 XSTS 令牌（附带游戏标签）
                var xsts = await DoPostJsonAsync<XstsResponse>
                    (XstsAuthorize, new Dictionary<string, object>
                    {
                        { "Properties", new Dictionary<string, object>
                            {
                                { "SandboxId", "RETAIL" },
                                { "UserTokens", new[] { xbl!.Token } }
                            } },
                        { "RelyingParty", "rp://api.minecraftservices.com/" },
                        { "TokenType", "JWT" }
                    }, msaAccessToken, needBearer: false);

                if (xsts == null || string.IsNullOrEmpty(xsts.Token))
                    return (null, "无法获取游戏服务授权（请确认该账号已购买 Minecraft）", true);

                var xui = xsts.DisplayClaims?.Xui;
                var gamerTag = (xui != null && xui.Count > 0) ? xui[0].Gtg : "";
                if (!string.IsNullOrEmpty(gamerTag))
                    uhs = xui[0].Uhs;

                // 3. 用 XSTS 令牌登录 Minecraft
                var mc = await DoPostJsonAsync<MinecraftAuthResponse>
                    (MinecraftLogin, new Dictionary<string, object>
                    {
                        { "identityToken", $"XBL3.0 x={uhs};{xsts.Token}" }
                    }, msaAccessToken, needBearer: false);

                if (mc == null || string.IsNullOrEmpty(mc.AccessToken))
                    return (null, "无法登录 Minecraft 服务", true);

                // 4. 获取角色资料（UUID / 名字）
                var profile = await DoGetJsonAsync<MinecraftProfile>
                    (MinecraftProfile, mc.AccessToken);

                if (profile == null || string.IsNullOrEmpty(profile.Name))
                    return (null, "未找到游戏角色，请先购买并创建角色", true);

                return (new MicrosoftLoginResult
                {
                    Username = profile.Name,
                    Uuid = profile.Id,
                    AccessToken = mc.AccessToken
                }, null, false);
            }
            catch (Exception ex)
            {
                return (null, ex.Message, true);
            }
        }

        // ============ HTTP 辅助 ============

        private static async Task<T?> DoPostJsonAsync<T>(
            string url, object body, string? msaToken = null, bool needBearer = true)
            where T : class
        {
            var node = System.Text.Json.JsonSerializer.SerializeToNode(body)!;
            var content = new StringContent(node.ToJsonString(),
                System.Text.Encoding.UTF8, "application/json");

            var msg = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            if (needBearer && !string.IsNullOrEmpty(msaToken))
                msg.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", msaToken);

            var resp = await _http.SendAsync(msg);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var errNode = TryGetNode(text);
                if (errNode?["XErr"] != null)
                {
                    long xerr = 0;
                    try { xerr = errNode["XErr"]!.GetValue<long>(); }
                    catch { }

                    switch (xerr)
                    {
                        case 2148916233:
                            throw new Exception("该账号未购买 Minecraft Java 版，请先购买游戏");
                        case 2148916238:
                            throw new Exception("无法登录 Minecraft，请确认已购买 Java 版并绑定到微软账号");
                        case 2148916235:
                            throw new Exception("您所在的地区无法使用该服务");
                        default:
                            throw new Exception($"Xbox 登录被拒绝（错误码 {xerr}）");
                    }
                }

                var desc = errNode?["error_description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(desc))
                    throw new Exception(desc);

                throw new Exception($"登录失败（HTTP {(int)resp.StatusCode}）");
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(text);
        }

        private static async Task<T?> DoGetJsonAsync<T>(string url, string bearer)
            where T : class
        {
            var msg = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(bearer))
                msg.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

            var resp = await _http.SendAsync(msg);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"获取资料失败（HTTP {(int)resp.StatusCode}）");

            return System.Text.Json.JsonSerializer.Deserialize<T>(text);
        }

        private static JsonNode? TryGetNode(string text)
        {
            try { return JsonNode.Parse(text); }
            catch { return null; }
        }
    }

    // ============ 模型 ============

    public class DeviceCodeInfo
    {
        public string DeviceCode { get; set; } = "";
        public string UserCode { get; set; } = "";
        public string VerificationUri { get; set; } = "";
        public int ExpiresIn { get; set; } = 900;
        public int Interval { get; set; } = 5;
    }

    public class MicrosoftLoginResult
    {
        public string Username { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string AccessToken { get; set; } = "";
    }

    public class MicrosoftAccessToken
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }

    public class XboxAuthResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("Token")]
        public string Token { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("DisplayClaims")]
        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    public class XstsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("Token")]
        public string Token { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("DisplayClaims")]
        public XboxDisplayClaims? DisplayClaims { get; set; }
    }

    public class XboxDisplayClaims
    {
        [System.Text.Json.Serialization.JsonPropertyName("xui")]
        public List<XuiClaim>? Xui { get; set; }
    }

    public class XuiClaim
    {
        [System.Text.Json.Serialization.JsonPropertyName("uhs")]
        public string Uhs { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("gtg")]
        public string Gtg { get; set; } = "";
    }

    public class MinecraftAuthResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";
    }

    public class MinecraftProfile
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}