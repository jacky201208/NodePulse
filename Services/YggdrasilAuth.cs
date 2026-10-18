using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class YggdrasilAuth
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public const string LittleSkinApi = "https://littleskin.cn/api/yggdrasil";
        public const string ElyByApi = "https://authserver.ely.by/api/authserver";

        public static async Task<(YggdrasilLoginResult? result, string? error)> LoginAsync(
            string apiRoot, string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(apiRoot))
                    return (null, "API Root 不能为空");
                if (string.IsNullOrWhiteSpace(username))
                    return (null, "用户名不能为空");
                if (string.IsNullOrWhiteSpace(password))
                    return (null, "密码不能为空");

                apiRoot = apiRoot.Trim().TrimEnd('/');

                var payload = new
                {
                    agent = new
                    {
                        name = "Minecraft",
                        version = 1
                    },
                    username = username,
                    password = password,
                    clientToken = Guid.NewGuid().ToString("N"),
                    requestUser = true
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"{apiRoot}/authserver/authenticate";
                var resp = await _http.PostAsync(url, content);
                var respText = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    string friendly = resp.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Forbidden => "用户名或密码错误",
                        System.Net.HttpStatusCode.BadRequest => "请求格式错误",
                        System.Net.HttpStatusCode.NotFound => "API Root 不正确（404）",
                        _ => $"HTTP {(int)resp.StatusCode}"
                    };

                    try
                    {
                        var errObj = JsonSerializer.Deserialize<YggdrasilError>(respText);
                        if (!string.IsNullOrEmpty(errObj?.ErrorMessage))
                            friendly = errObj.ErrorMessage;
                    }
                    catch { }

                    return (null, friendly);
                }

                var raw = JsonSerializer.Deserialize<YggdrasilRawResponse>(respText);

                if (raw == null || raw.AvailableProfiles.Count == 0)
                    return (null, "服务器未返回任何角色信息，请先到皮肤站创建一个角色");

                var profiles = new List<ProfileInfo>();
                foreach (var p in raw.AvailableProfiles)
                {
                    profiles.Add(new ProfileInfo
                    {
                        Id = p.Id,
                        Name = p.Name
                    });
                }

                var result = new YggdrasilLoginResult
                {
                    AccessToken = raw.AccessToken,
                    ClientToken = raw.ClientToken,
                    AvailableProfiles = profiles,
                    SelectedProfile = raw.SelectedProfile != null
                        ? new ProfileInfo { Id = raw.SelectedProfile.Id, Name = raw.SelectedProfile.Name }
                        : profiles[0]
                };

                return (result, null);
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

        public static string GuessServerName(string apiRoot)
        {
            if (apiRoot.Contains("littleskin", StringComparison.OrdinalIgnoreCase))
                return "LittleSkin";
            if (apiRoot.Contains("ely.by", StringComparison.OrdinalIgnoreCase))
                return "Ely.by";

            try
            {
                var uri = new Uri(apiRoot);
                return uri.Host;
            }
            catch
            {
                return "外置";
            }
        }
    }

    // ============ JSON 解析模型 ============

    public class YggdrasilLoginResult
    {
        public string AccessToken { get; set; } = "";
        public string ClientToken { get; set; } = "";
        public List<ProfileInfo> AvailableProfiles { get; set; } = new();
        public ProfileInfo? SelectedProfile { get; set; }
    }

    public class YggdrasilRawResponse
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("clientToken")]
        public string ClientToken { get; set; } = "";

        [JsonPropertyName("availableProfiles")]
        public List<YggdrasilProfile> AvailableProfiles { get; set; } = new();

        [JsonPropertyName("selectedProfile")]
        public YggdrasilProfile? SelectedProfile { get; set; }
    }

    public class YggdrasilProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }

    public class YggdrasilError
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = "";

        [JsonPropertyName("errorMessage")]
        public string ErrorMessage { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}