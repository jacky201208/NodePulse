using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NodePulse.Services
{
    public static class QuiltInstaller
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        static QuiltInstaller()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");
        }

        private static readonly string[] ProfileUrlTemplates =
        {
            "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json",
            "https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/{0}/{1}/profile/json"
        };

        // ================================================================
        // 生成 Quilt profile JSON：拉取 Quilt profile + 原版 JSON，合并成独立版本
        // ================================================================

        public static async Task GenerateProfileAsync(
            string mcVersion, string loaderVersion, string targetName)
        {
            var quiltJson = await FetchQuiltProfileAsync(mcVersion, loaderVersion);
            var vanillaJson = await FetchVanillaVersionJsonAsync(mcVersion);
            var mergedJson = MergeVersionJson(vanillaJson, quiltJson, targetName);

            var targetDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", targetName);
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, targetName + ".json");
            File.WriteAllText(targetFile,
                mergedJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                System.Text.Encoding.UTF8);
        }

        private static async Task<string> FetchQuiltProfileAsync(
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
                        if (retry == 0) await Task.Delay(500);
                    }
                }
            }

            throw new Exception(
                $"无法获取 Quilt profile\n最后错误：{lastEx?.Message}");
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

        private static JsonObject MergeVersionJson(
            string vanillaJson, string quiltJson, string targetName)
        {
            var vanilla = JsonNode.Parse(vanillaJson)?.AsObject()
                ?? throw new Exception("原版 JSON 解析失败");
            var quilt = JsonNode.Parse(quiltJson)?.AsObject()
                ?? throw new Exception("Quilt JSON 解析失败");

            var mergedLibs = new JsonArray();

            if (vanilla["libraries"] is JsonArray vLibs)
                foreach (var lib in vLibs)
                    mergedLibs.Add(lib?.DeepClone());

            if (quilt["libraries"] is JsonArray qLibs)
                foreach (var lib in qLibs)
                    mergedLibs.Add(lib?.DeepClone());

            var mergedJvm = new JsonArray();
            var mergedGame = new JsonArray();

            if (vanilla["arguments"] is JsonObject vArgs)
            {
                if (vArgs["jvm"] is JsonArray vJvm)
                    foreach (var a in vJvm) mergedJvm.Add(a?.DeepClone());
                if (vArgs["game"] is JsonArray vGame)
                    foreach (var a in vGame) mergedGame.Add(a?.DeepClone());
            }

            if (quilt["arguments"] is JsonObject qArgs)
            {
                if (qArgs["jvm"] is JsonArray qJvm)
                    foreach (var a in qJvm) mergedJvm.Add(a?.DeepClone());
                if (qArgs["game"] is JsonArray qGame)
                    foreach (var a in qGame) mergedGame.Add(a?.DeepClone());
            }

            var mergedArgs = new JsonObject
            {
                ["jvm"] = mergedJvm,
                ["game"] = mergedGame
            };

            var result = new JsonObject
            {
                ["id"] = targetName,
                ["type"] = "release",
                ["mainClass"] = quilt["mainClass"]?.GetValue<string>() ?? "",
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
    }
}