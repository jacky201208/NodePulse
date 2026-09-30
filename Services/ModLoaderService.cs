using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NodePulse.Services
{
    public static class ModLoaderService
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static ModLoaderService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");
        }

        // ================================================================
        // Fabric Loader
        // ================================================================

        public static async Task<List<string>> GetFabricLoaderVersionsAsync(string mcVersion)
        {
            var urls = new[]
            {
                $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}",
                $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mcVersion}"
            };

            foreach (var url in urls)
            {
                try
                {
                    var json = await _http.GetStringAsync(url);
                    var arr = JsonNode.Parse(json)?.AsArray();
                    if (arr == null || arr.Count == 0) continue;

                    var result = new List<string>();
                    foreach (var item in arr)
                    {
                        var v = item?["loader"]?["version"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(v))
                            result.Add(v);
                    }

                    if (result.Count > 0)
                        return result;
                }
                catch { }
            }

            return new();
        }

        // ================================================================
        // Fabric API
        // ================================================================

        public static async Task<List<string>> GetFabricApiVersionsAsync(string mcVersion)
        {
            try
            {
                var url = $"https://api.modrinth.com/v2/project/fabric-api/version" +
                          $"?game_versions=%5B%22{mcVersion}%22%5D" +
                          $"&loaders=%5B%22fabric%22%5D";

                var json = await _http.GetStringAsync(url);
                var arr = JsonNode.Parse(json)?.AsArray();
                if (arr == null || arr.Count == 0) return new();

                var result = new List<string>();
                foreach (var item in arr)
                {
                    var v = item?["version_number"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(v))
                        result.Add(v);
                }
                return result;
            }
            catch
            {
                return new();
            }
        }

        // ================================================================
        // Quilt Loader
        // ================================================================

        public static async Task<List<string>> GetQuiltLoaderVersionsAsync(string mcVersion)
        {
            try
            {
                var url = $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}";
                var json = await _http.GetStringAsync(url);
                var arr = JsonNode.Parse(json)?.AsArray();
                if (arr == null || arr.Count == 0) return new();

                var result = new List<string>();
                foreach (var item in arr)
                {
                    var v = item?["loader"]?["version"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(v))
                        result.Add(v);
                }
                return result;
            }
            catch
            {
                return new();
            }
        }

        // ================================================================
        // Forge
        // ================================================================

        public static async Task<List<string>> GetForgeVersionsAsync(string mcVersion)
        {
            var candidates = await FetchForgeCandidatesAsync(mcVersion);
            if (candidates.Count == 0) return new();

            var toVerify = candidates.Take(60).ToList();
            var verified = await VerifyForgeVersionsAsync(mcVersion, toVerify);
            verified.Sort((a, b) => CompareVersions(b, a));

            if (verified.Count >= 3 || verified.Count >= candidates.Count / 2)
                return verified;

            return candidates;
        }

        private static async Task<List<string>> FetchForgeCandidatesAsync(string mcVersion)
        {
            var urls = new[]
            {
                "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.xml",
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml",
                "https://files.minecraftforge.net/maven/net/minecraftforge/forge/maven-metadata.xml"
            };

            foreach (var url in urls)
            {
                try
                {
                    var xml = await _http.GetStringAsync(url);
                    var versions = ParseForgeMavenVersions(xml, mcVersion);
                    if (versions.Count > 0)
                        return versions;
                }
                catch { }
            }

            return new();
        }

        private static List<string> ParseForgeMavenVersions(string xml, string mcVersion)
        {
            var result = new List<string>();
            try
            {
                var doc = XDocument.Parse(xml);
                var elements = doc.Descendants("version");
                string prefix = mcVersion + "-";

                foreach (var el in elements)
                {
                    var full = el.Value?.Trim();
                    if (string.IsNullOrEmpty(full)) continue;
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var ver = full.Substring(prefix.Length);
                    if (!Regex.IsMatch(ver, @"^\d+\.\d+(\.\d+)?(\.\d+)?$"))
                        continue;

                    result.Add(ver);
                }
            }
            catch { return result; }

            result.Sort((a, b) => CompareVersions(b, a));
            return result.Distinct().ToList();
        }

        private static async Task<List<string>> VerifyForgeVersionsAsync(
            string mcVersion, List<string> candidates)
        {
            if (candidates.Count == 0) return new();

            var verified = new List<string>();
            var lockObj = new object();
            using var semaphore = new SemaphoreSlim(32);

            var tasks = candidates.Select(async v =>
            {
                try
                {
                    await semaphore.WaitAsync().ConfigureAwait(false);
                    if (await CheckForgeInstallerExistsAsync(mcVersion, v))
                    {
                        lock (lockObj) { verified.Add(v); }
                    }
                }
                catch { }
                finally
                {
                    try { semaphore.Release(); } catch { }
                }
            }).ToList();

            await Task.WhenAll(tasks);
            return verified;
        }

        private static async Task<bool> CheckForgeInstallerExistsAsync(
            string mcVersion, string forgeVersion)
        {
            var combined = $"{mcVersion}-{forgeVersion}";

            var bmclUrl = $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/" +
                          $"{combined}/forge-{combined}-installer.jar";
            var bmclResult = await RangeCheckAsync(bmclUrl);

            if (bmclResult == CheckResult.Exists) return true;

            if (bmclResult == CheckResult.NotFound)
            {
                var officialUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/" +
                                  $"{combined}/forge-{combined}-installer.jar";
                var officialResult = await RangeCheckAsync(officialUrl);
                return officialResult == CheckResult.Exists;
            }

            return true;
        }

        private enum CheckResult { Exists, NotFound, NetworkError }

        private static async Task<CheckResult> RangeCheckAsync(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(0, 0);

                using var resp = await _http.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return CheckResult.NotFound;

                if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.PartialContent)
                    return CheckResult.Exists;

                return CheckResult.NetworkError;
            }
            catch
            {
                return CheckResult.NetworkError;
            }
        }

        // ================================================================
        // NeoForge
        // ================================================================

        public static async Task<List<string>> GetNeoForgeVersionsAsync(string mcVersion)
        {
            bool isLegacy = mcVersion == "1.20.1";

            string[] urls;
            if (isLegacy)
            {
                urls = new[]
                {
                    "https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/maven-metadata.xml",
                    "https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml"
                };
            }
            else
            {
                urls = new[]
                {
                    "https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/maven-metadata.xml",
                    "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"
                };
            }

            foreach (var url in urls)
            {
                try
                {
                    var xml = await _http.GetStringAsync(url);
                    var versions = ParseNeoForgeMavenVersions(xml, mcVersion, isLegacy);
                    if (versions.Count > 0)
                        return versions;
                }
                catch { }
            }

            return new();
        }

        private static List<string> ParseNeoForgeMavenVersions(
            string xml, string mcVersion, bool isLegacy)
        {
            var result = new List<string>();
            try
            {
                var doc = XDocument.Parse(xml);
                var elements = doc.Descendants("version");

                if (isLegacy)
                {
                    string prefix = mcVersion + "-";
                    foreach (var el in elements)
                    {
                        var full = el.Value?.Trim();
                        if (string.IsNullOrEmpty(full)) continue;
                        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var ver = full.Substring(prefix.Length);
                        if (!Regex.IsMatch(ver, @"^\d+\.\d+(\.\d+)?$"))
                            continue;

                        result.Add(ver);
                    }
                }
                else
                {
                    var (minor, patch) = ParseMc(mcVersion);
                    string prefix = $"{minor}.{patch}.";

                    foreach (var el in elements)
                    {
                        var full = el.Value?.Trim();
                        if (string.IsNullOrEmpty(full)) continue;
                        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!Regex.IsMatch(full, @"^\d+\.\d+(\.\d+)?$"))
                            continue;

                        result.Add(full);
                    }
                }
            }
            catch { return result; }

            result.Sort((a, b) => CompareVersions(b, a));
            return result.Distinct().ToList();
        }

        // ================================================================
        // OptiFine
        // ================================================================

        public static async Task<List<string>> GetOptiFineVersionsAsync(string mcVersion)
        {
            try
            {
                var url = $"https://bmclapi2.bangbang93.com/optifine/{mcVersion}";
                var json = await _http.GetStringAsync(url);
                var arr = JsonNode.Parse(json)?.AsArray();

                if (arr != null && arr.Count > 0)
                {
                    var result = new List<string>();
                    foreach (var item in arr)
                    {
                        var type = item?["type"]?.GetValue<string>() ?? "";
                        var patch = item?["patch"]?.GetValue<string>() ?? "";
                        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(patch))
                            continue;

                        result.Add($"{type}_{patch}");
                    }

                    if (result.Count > 0)
                        return result.Distinct().ToList();
                }
            }
            catch { }

            try
            {
                var url = "https://bmclapi2.bangbang93.com/optifine/versionList";
                var json = await _http.GetStringAsync(url);
                var arr = JsonNode.Parse(json)?.AsArray();

                if (arr != null && arr.Count > 0)
                {
                    var result = new List<string>();
                    foreach (var item in arr)
                    {
                        var mcVer = item?["mcversion"]?.GetValue<string>();
                        if (!string.Equals(mcVer, mcVersion, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var type = item?["type"]?.GetValue<string>() ?? "";
                        var patch = item?["patch"]?.GetValue<string>() ?? "";
                        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(patch))
                            continue;

                        result.Add($"{type}_{patch}");
                    }

                    if (result.Count > 0)
                        return result.Distinct().ToList();
                }
            }
            catch { }

            return new();
        }

        // ================================================================
        // 工具
        // ================================================================

        private static (int minor, int patch) ParseMc(string v)
        {
            if (string.IsNullOrEmpty(v))
                return (0, 0);

            if (v.StartsWith("1."))
            {
                var s = v.Substring(2);
                var parts = s.Split('.');
                int minor = 0, patch = 0;
                if (parts.Length >= 1 && int.TryParse(parts[0], out var m)) minor = m;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var p)) patch = p;
                return (minor, patch);
            }

            var parts2 = v.Split('.');
            int minor2 = 0, patch2 = 0;
            if (parts2.Length >= 1 && int.TryParse(parts2[0], out var a)) minor2 = a;
            if (parts2.Length >= 2 && int.TryParse(parts2[1], out var b)) patch2 = b;
            return (minor2, patch2);
        }

        private static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var pb = b.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();

            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }
    }
}