using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NodePulse.Services
{
    public static class FabricInstaller
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        static FabricInstaller()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");
        }

        private static readonly string[] ProfileUrlTemplates =
        {
            "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json",
            "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{0}/{1}/profile/json"
        };

        // ================================================================
        // 生成 Fabric profile JSON：拉取 Fabric profile + 原版 JSON，合并成独立版本
        // ================================================================
        public static async Task GenerateProfileAsync(
            string mcVersion, string loaderVersion, string targetName)
        {
            // 1. 拉取 Fabric profile
            var fabricJson = await FetchFabricProfileAsync(mcVersion, loaderVersion);

            // 2. 拉取原版 JSON
            var vanillaJson = await FetchVanillaVersionJsonAsync(mcVersion);

            // 3. 合并成独立版本
            var mergedJson = MergeVersionJson(vanillaJson, fabricJson, targetName);

            // 4. 保存到 versions/{targetName}/{targetName}.json
            var targetDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", targetName);
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, targetName + ".json");
            File.WriteAllText(targetFile,
                mergedJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                System.Text.Encoding.UTF8);
        }

        // ================================================================
        // 拉取
        // ================================================================

        private static async Task<string> FetchFabricProfileAsync(
            string mcVersion, string loaderVersion)
        {
            Exception? lastEx = null;

            foreach (var template in ProfileUrlTemplates)
            {
                var url = string.Format(template, mcVersion, loaderVersion);

                for (int retry = 0; retry < 2; retry++)
                {
                    try
                    {
                        var json = await _http.GetStringAsync(url);
                        if (!string.IsNullOrEmpty(json)) return json;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        System.Diagnostics.Debug.WriteLine(
                            $"[FabricInstaller] {url} 失败: {ex.Message}");
                        if (retry == 0) await Task.Delay(500);
                    }
                }
            }

            throw new Exception(
                $"无法获取 Fabric profile\n最后错误：{lastEx?.Message}\n" +
                $"提示：请检查网络连接，或尝试切换 VPN");
        }

        private static async Task<string> FetchVanillaVersionJsonAsync(string mcVersion)
        {
            var versions = await VersionDownloader.GetVersionListAsync();
            var entry = versions.FirstOrDefault(v => v.Id == mcVersion);

            if (entry == null || string.IsNullOrEmpty(entry.Url))
                throw new Exception($"版本清单里找不到原版 {mcVersion}");

            try
            {
                return await _http.GetStringAsync(entry.Url);
            }
            catch
            {
                var mirror = VersionDownloader.ToMirrorUrl(entry.Url);
                if (mirror != entry.Url)
                    return await _http.GetStringAsync(mirror);
                throw;
            }
        }

        // ================================================================
        // 合并原版 + Fabric，生成独立版本 JSON
        // ================================================================

        private static JsonObject MergeVersionJson(
            string vanillaJson, string fabricJson, string targetName)
        {
            var vanilla = JsonNode.Parse(vanillaJson)?.AsObject()
                ?? throw new Exception("原版 JSON 解析失败");
            var fabric = JsonNode.Parse(fabricJson)?.AsObject()
                ?? throw new Exception("Fabric JSON 解析失败");

            // libraries：原版 + Fabric
            var mergedLibs = new JsonArray();

            if (vanilla["libraries"] is JsonArray vLibs)
                foreach (var lib in vLibs)
                    mergedLibs.Add(lib?.DeepClone());

            if (fabric["libraries"] is JsonArray fLibs)
                foreach (var lib in fLibs)
                    mergedLibs.Add(lib?.DeepClone());

            // arguments.jvm / game：原版 + Fabric
            var mergedJvm = new JsonArray();
            var mergedGame = new JsonArray();

            if (vanilla["arguments"] is JsonObject vArgs)
            {
                if (vArgs["jvm"] is JsonArray vJvm)
                    foreach (var a in vJvm) mergedJvm.Add(a?.DeepClone());
                if (vArgs["game"] is JsonArray vGame)
                    foreach (var a in vGame) mergedGame.Add(a?.DeepClone());
            }

            if (fabric["arguments"] is JsonObject fArgs)
            {
                if (fArgs["jvm"] is JsonArray fJvm)
                    foreach (var a in fJvm) mergedJvm.Add(a?.DeepClone());
                if (fArgs["game"] is JsonArray fGame)
                    foreach (var a in fGame) mergedGame.Add(a?.DeepClone());
            }

            var mergedArgs = new JsonObject
            {
                ["jvm"] = mergedJvm,
                ["game"] = mergedGame
            };

            // 构造最终 JSON（无 inheritsFrom）
            var result = new JsonObject
            {
                ["id"] = targetName,
                ["type"] = "release",
                ["mainClass"] = fabric["mainClass"]?.GetValue<string>() ?? "",
                ["assets"] = vanilla["assets"]?.DeepClone() ?? "5",
                ["assetIndex"] = vanilla["assetIndex"]?.DeepClone(),
                ["downloads"] = vanilla["downloads"]?.DeepClone(),
                ["javaVersion"] = vanilla["javaVersion"]?.DeepClone(),
                ["libraries"] = mergedLibs,
                ["arguments"] = mergedArgs
            };

            if (vanilla["releaseTime"] != null)
                result["releaseTime"] = vanilla["releaseTime"]!.DeepClone();
            if (vanilla["time"] != null)
                result["time"] = vanilla["time"]!.DeepClone();

            return result;
        }

        // ================================================================
        // Fabric API 下载到 versions/{targetName}/mods/
        // ================================================================

        /// <summary>
        /// 下载 Fabric API 到 mods/。
        /// </summary>
        /// <param name="mcVersion">Minecraft 版本（如 1.20.1）</param>
        /// <param name="targetName">实例名（版本目录名）</param>
        /// <param name="preferredVersion">用户选定的 API 版本号，留空则自动选最新</param>
        public static async Task<bool> InstallFabricApiAsync(
            string mcVersion,
            string targetName,
            string preferredVersion = "")
        {
            try
            {
                var modsDir = Path.Combine(
                    VersionScanner.MinecraftFolder, "versions", targetName, "mods");
                Directory.CreateDirectory(modsDir);

                var existing = Directory.GetFiles(modsDir, "fabric-api-*.jar");
                if (existing.Length > 0) return true;

                // Modrinth API 查询
                var url = $"https://api.modrinth.com/v2/project/fabric-api/version" +
                          $"?game_versions=%5B%22{mcVersion}%22%5D" +
                          $"&loaders=%5B%22fabric%22%5D";

                var json = await _http.GetStringAsync(url);
                var arr = JsonNode.Parse(json)?.AsArray();
                if (arr == null || arr.Count == 0) return false;

                // 优先匹配用户选的版本
                JsonNode? chosen = null;

                if (!string.IsNullOrEmpty(preferredVersion))
                {
                    foreach (var v in arr)
                    {
                        var ver = v?["version_number"]?.GetValue<string>();
                        if (ver == preferredVersion)
                        {
                            chosen = v;
                            break;
                        }
                    }
                }

                chosen ??= arr[0];

                var files = chosen?["files"]?.AsArray();
                if (files == null || files.Count == 0) return false;

                string? downloadUrl = null;
                string? fileName = null;

                foreach (var file in files)
                {
                    if (file?["primary"]?.GetValue<bool>() ?? false)
                    {
                        downloadUrl = file?["url"]?.GetValue<string>();
                        fileName = file?["filename"]?.GetValue<string>();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(fileName))
                    return false;

                var target = Path.Combine(modsDir, fileName);

                // BMCLAPI 镜像 Modrinth
                var mirrorUrl = downloadUrl.Replace(
                    "https://cdn.modrinth.com/",
                    "https://bmclapi2.bangbang93.com/");

                byte[] bytes;
                try
                {
                    bytes = await _http.GetByteArrayAsync(mirrorUrl);
                }
                catch
                {
                    bytes = await _http.GetByteArrayAsync(downloadUrl);
                }

                await File.WriteAllBytesAsync(target, bytes);

                System.Diagnostics.Debug.WriteLine(
                    $"[FabricAPI] 已下载 {fileName}");

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FabricAPI] 下载失败: {ex.Message}");
                return false;
            }
        }
    }
}