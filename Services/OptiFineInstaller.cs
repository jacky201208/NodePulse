using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class OptiFineInstaller
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromMinutes(3)
        };

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private static readonly JsonSerializerOptions JsonWriteOpts = new()
        {
            WriteIndented = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        private static readonly string InstallLogPath =
            Path.Combine(AppContext.BaseDirectory, "optifine-install.log");

        static OptiFineInstaller()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");
        }

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(InstallLogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}",
                    Utf8NoBom);
            }
            catch { }
            Debug.WriteLine($"[OptiFineInstaller] {msg}");
        }

        // ================================================================
        // 版本解析与 URL 构造
        // ================================================================

        public static (string type, string patch) SplitVersion(string ofVersion)
        {
            var idx = ofVersion.IndexOf('_', ofVersion.IndexOf('_') + 1);
            if (idx < 0)
                return ("HD_U", ofVersion);
            return (ofVersion.Substring(0, idx), ofVersion.Substring(idx + 1));
        }

        public static string GetInstallerUrl(string mcVersion, string type, string patch)
        {
            return $"https://bmclapi2.bangbang93.com/optifine/{mcVersion}/{type}/{patch}";
        }

        // ================================================================
        // 下载 installer.jar
        // ================================================================

        public static async Task<string> DownloadInstallerAsync(
            string mcVersion, string ofVersion,
            IProgress<DownloadProgress>? progress = null)
        {
            var (type, patch) = SplitVersion(ofVersion);

            var cacheDir = Path.Combine(VersionScanner.MinecraftFolder, "installers");
            Directory.CreateDirectory(cacheDir);

            var fileName = $"OptiFine_{mcVersion}_{type}_{patch}.jar";
            var targetPath = Path.Combine(cacheDir, fileName);

            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 100_000)
            {
                Log($"[Step1] installer 已缓存：{targetPath}");
                return targetPath;
            }

            var url = GetInstallerUrl(mcVersion, type, patch);
            Log($"[Step1] 下载 installer：{url}");

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 OptiFine 安装器",
                CurrentFile = $"下载 OptiFine {ofVersion}...",
                Done = 0, Total = 100
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? 0;
            long read = 0;

            using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

            var buf = new byte[81920];
            int n;
            var last = DateTime.MinValue;

            while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                read += n;

                var now = DateTime.UtcNow;
                if ((now - last).TotalMilliseconds >= 200)
                {
                    last = now;
                    string sz = totalBytes > 0
                        ? $"({read / 1024.0 / 1024:F1}/{totalBytes / 1024.0 / 1024:F1} MB)"
                        : $"({read / 1024.0 / 1024:F1} MB)";
                    progress?.Report(new DownloadProgress
                    {
                        Stage = "下载 OptiFine 安装器",
                        CurrentFile = $"下载 OptiFine {ofVersion}... {sz}",
                        Done = (int)(read / 1024),
                        Total = totalBytes > 0 ? (int)(totalBytes / 1024) : 0
                    });
                }
            }
            await dst.FlushAsync(cts.Token);

            if (new FileInfo(targetPath).Length < 100_000)
                throw new Exception("OptiFine 安装器文件过小，可能下载失败");

            return targetPath;
        }

        // ================================================================
        // 主流程：抽取 + 手写 JSON（PCL2 方案）
        // ================================================================

        public static async Task InstallAsync(
            string mcVersion, string ofVersion, string targetName,
            IProgress<DownloadProgress>? progress = null)
        {
            try { File.WriteAllText(InstallLogPath, "", Utf8NoBom); } catch { }

            Log($"=== 安装开始 ===");
            Log($"mcVersion = {mcVersion}");
            Log($"ofVersion = {ofVersion}");
            Log($"targetName = {targetName}");

            var mcFolder = VersionScanner.MinecraftFolder;
            var targetDir = Path.Combine(mcFolder, "versions", targetName);
            var libDir = Path.Combine(mcFolder, "libraries");

            Log($"mcFolder = {mcFolder}");
            Log($"targetDir = {targetDir}");
            Log($"libDir = {libDir}");

            if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir).Length > 0)
                throw new Exception(
                    $"目标版本目录已存在且非空：{targetName}\n\n请返回修改游戏名称。");

            try
            {
                // ---------- 1. 下载 OptiFine installer jar ----------
                progress?.Report(new DownloadProgress
                {
                    Stage = "下载 OptiFine 安装器",
                    CurrentFile = "下载 OptiFine 安装器...",
                    Done = 5, Total = 100
                });

                var installerPath = await DownloadInstallerAsync(mcVersion, ofVersion, progress);

                progress?.Report(new DownloadProgress
                {
                    Stage = "下载 OptiFine 安装器",
                    CurrentFile = "OptiFine 安装器下载完成",
                    Done = 15, Total = 100
                });

                // ---------- 2. 下载原版 JSON 和 client.jar ----------
                Log($"[Step2] 获取原版 {mcVersion} 信息...");

                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版信息",
                    CurrentFile = $"正在获取原版 {mcVersion} 信息...",
                    Done = 20, Total = 100
                });

                var list = await VersionDownloader.GetVersionListAsync();
                Log($"[Step2] 版本清单获取成功，共 {list.Count} 个版本");

                var entry = list.FirstOrDefault(v => v.Id == mcVersion)
                    ?? throw new Exception($"Mojang 版本清单里找不到原版 {mcVersion}");
                Log($"[Step2] 找到原版 {mcVersion}，JSON URL = {entry.Url}");

                var vanillaJsonText = await GetStringWithFallback(entry.Url);
                var vanillaRoot = JsonNode.Parse(vanillaJsonText)?.AsObject()
                    ?? throw new Exception("原版 JSON 解析失败");
                Log($"[Step2] 原版 JSON 解析成功，大小 {vanillaJsonText.Length} 字符");

                var clientUrl = vanillaRoot["downloads"]?["client"]?["url"]?.GetValue<string>();
                var clientSize = vanillaRoot["downloads"]?["client"]?["size"]?.GetValue<long>() ?? 0;

                if (string.IsNullOrEmpty(clientUrl))
                    throw new Exception("原版 JSON 缺少 client.jar 下载地址");

                Directory.CreateDirectory(targetDir);
                Log($"[Step2] 已创建目标目录：{targetDir}");

                var targetJar = Path.Combine(targetDir, targetName + ".jar");
                var targetJson = Path.Combine(targetDir, targetName + ".json");

                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版信息",
                    CurrentFile = $"下载原版 {mcVersion} 客户端...",
                    Done = 25, Total = 100
                });

                Log($"[Step2] 开始下载 client.jar → {targetJar}");
                await DownloadToFileAsync(clientUrl, targetJar, clientSize, progress);
                Log($"[Step2] client.jar 下载完成，大小 {new FileInfo(targetJar).Length}");

                // ---------- 3. 抽取 optifine/** ----------
                Log($"[Step3] 开始抽取 optifine/**...");

                progress?.Report(new DownloadProgress
                {
                    Stage = "安装 OptiFine",
                    CurrentFile = "正在抽取 OptiFine 运行时文件...",
                    Done = 50, Total = 100
                });

                var ofLibraryVersion = $"{mcVersion}_{ofVersion}";
                var ofJarDir = Path.Combine(libDir, "optifine", "OptiFine", ofLibraryVersion);
                Directory.CreateDirectory(ofJarDir);
                var ofJarPath = Path.Combine(ofJarDir, $"OptiFine-{ofLibraryVersion}.jar");
                Log($"[Step3] 抽取目标：{ofJarPath}");

                int ofEntryCount = ExtractJarSubset(installerPath, ofJarPath, "optifine/");
                Log($"[Step3] 抽取了 {ofEntryCount} 个 optifine/ 条目");

                if (ofEntryCount == 0)
                {
                    var topDirs = new HashSet<string>();
                    try
                    {
                        using var zip = ZipFile.OpenRead(installerPath);
                        foreach (var e in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(e.Name)) continue;
                            var slash = e.FullName.IndexOf('/');
                            if (slash > 0)
                                topDirs.Add(e.FullName.Substring(0, slash));
                            else
                                topDirs.Add("(root) " + e.FullName);
                        }
                    }
                    catch { }

                    Log($"[Step3] installer 顶层目录：{string.Join(", ", topDirs)}");
                    throw new Exception(
                        "OptiFine installer 里没有 optifine/ 目录，无法抽取。\n" +
                        $"顶层目录：{string.Join(", ", topDirs)}");
                }

                // ---------- 3.2 释放 launchwrapper-of ----------
                Log($"[Step4] 释放 launchwrapper-of...");

                progress?.Report(new DownloadProgress
                {
                    Stage = "安装 OptiFine",
                    CurrentFile = "正在释放 launchwrapper-of...",
                    Done = 65, Total = 100
                });

                var lwSourcePath = await OptiFineBridgeManager.EnsureLaunchwrapperAsync();
                Log($"[Step4] launchwrapper-of 源文件：{lwSourcePath}");

                var lwVersion = OptiFineBridgeManager.LaunchwrapperVersion;
                var lwJarDir = Path.Combine(libDir, "optifine", "launchwrapper-of", lwVersion);
                Directory.CreateDirectory(lwJarDir);
                var lwJarPath = Path.Combine(lwJarDir, $"launchwrapper-of-{lwVersion}.jar");

                try
                {
                    File.Copy(lwSourcePath, lwJarPath, overwrite: true);
                    Log($"[Step4] launchwrapper-of 复制到：{lwJarPath}");
                }
                catch (Exception ex)
                {
                    throw new Exception($"复制 launchwrapper-of 到 libraries 失败：{ex.Message}", ex);
                }

                // ---------- 4. 生成版本 JSON ----------
                Log($"[Step5] 生成版本 JSON...");

                progress?.Report(new DownloadProgress
                {
                    Stage = "配置 OptiFine 实例",
                    CurrentFile = "生成版本 JSON...",
                    Done = 75, Total = 100
                });

                var outputJson = BuildOptiFineJson(
                    vanillaRoot, targetName, mcVersion, ofVersion, ofLibraryVersion);

                var jsonText = outputJson.ToJsonString(JsonWriteOpts);
                File.WriteAllText(targetJson, jsonText, Utf8NoBom);
                Log($"[Step5] JSON 已写入：{targetJson}，大小 {jsonText.Length} 字符");

                // ---------- 5. 校验补全 ----------
                Log($"[Step6] 调用 ValidateAndRepairAsync...");

                progress?.Report(new DownloadProgress
                {
                    Stage = "校验游戏文件",
                    CurrentFile = "校验并补全游戏资源...",
                    Done = 90, Total = 100
                });

                try
                {
                    await MinecraftLauncher.ValidateAndRepairAsync(targetName, progress);
                    Log($"[Step6] ValidateAndRepairAsync 完成");
                }
                catch (Exception ex)
                {
                    Log($"[Step6] ValidateAndRepairAsync 失败（忽略）：{ex.Message}");
                }

                Log($"[Done] OptiFine {ofVersion} 安装完成");

                progress?.Report(new DownloadProgress
                {
                    Stage = "已完成",
                    CurrentFile = $"OptiFine {ofVersion} 安装完成",
                    Done = 100, Total = 100
                });
            }
            catch (Exception ex)
            {
                Log($"[ERROR] 安装失败：{ex.GetType().Name}: {ex.Message}");
                Log($"[ERROR] 堆栈：{ex.StackTrace}");
                throw;
            }
        }

        // ================================================================
        // 从源 jar 抽取
        // ================================================================

        private static int ExtractJarSubset(
            string srcJarPath, string dstJarPath, string prefix)
        {
            var dstDir = Path.GetDirectoryName(dstJarPath);
            if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);

            int count = 0;

            try
            {
                using var srcZip = ZipFile.OpenRead(srcJarPath);
                using var dstStream = new FileStream(
                    dstJarPath, FileMode.Create, FileAccess.Write);
                using var dstZip = new ZipArchive(dstStream, ZipArchiveMode.Create);

                foreach (var entry in srcZip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var newEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var src = entry.Open();
                    using var dst = newEntry.Open();
                    src.CopyTo(dst);
                    count++;
                }
            }
            catch (Exception ex)
            {
                Log($"[ExtractJarSubset] 抽取失败：{ex.Message}");
                throw;
            }

            return count;
        }

        // ================================================================
        // 生成 OptiFine 版本 JSON
        // ================================================================

        private static JsonObject BuildOptiFineJson(
            JsonObject vanillaRoot,
            string targetName,
            string mcVersion,
            string ofVersion,
            string ofLibraryVersion)
        {
            var result = (JsonObject)vanillaRoot.DeepClone();

            result["id"] = targetName;
            result["mainClass"] = "net.minecraft.launchwrapper.Launch";
            result.Remove("inheritsFrom");

            if (result["arguments"] is JsonObject args)
            {
                if (args["game"] is JsonArray gameArr)
                {
                    gameArr.Add("--tweakClass");
                    gameArr.Add("optifine.OptiFineTweaker");
                }
            }

            if (result["libraries"] is JsonArray libs)
            {
                libs.Add(new JsonObject
                {
                    ["name"] = $"optifine:OptiFine:{ofLibraryVersion}"
                });
                libs.Add(new JsonObject
                {
                    ["name"] = $"optifine:launchwrapper-of:{OptiFineBridgeManager.LaunchwrapperVersion}"
                });
            }

            if (result["downloads"] is JsonObject dl)
            {
                dl.Remove("client");
                dl.Remove("server");
                dl.Remove("client_mappings");
                dl.Remove("server_mappings");
            }

            return result;
        }

        // ================================================================
        // HTTP 下载辅助
        // ================================================================

        private static async Task<string> GetStringWithFallback(string officialUrl)
        {
            try
            {
                return await _http.GetStringAsync(officialUrl);
            }
            catch
            {
                var mirror = VersionDownloader.ToMirrorUrl(officialUrl);
                if (mirror != officialUrl)
                    return await _http.GetStringAsync(mirror);
                throw;
            }
        }

        private static async Task DownloadToFileAsync(
            string url, string targetPath, long expectedSize,
            IProgress<DownloadProgress>? progress = null)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var urls = new[] { url, VersionDownloader.ToMirrorUrl(url) }
                .Where(u => !string.IsNullOrEmpty(u))
                .Distinct()
                .ToList();

            Exception? lastEx = null;
            foreach (var u in urls)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    using var resp = await _http.GetAsync(
                        u, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();

                    long total = resp.Content.Headers.ContentLength ?? expectedSize;
                    long read = 0;

                    using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
                    using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

                    var buf = new byte[81920];
                    int n;
                    var last = DateTime.MinValue;

                    while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                        read += n;

                        var now = DateTime.UtcNow;
                        if ((now - last).TotalMilliseconds >= 200)
                        {
                            last = now;
                            string sz = total > 0
                                ? $"{read / 1024.0 / 1024:F1}/{total / 1024.0 / 1024:F1} MB"
                                : $"{read / 1024.0 / 1024:F1} MB";
                            progress?.Report(new DownloadProgress
                            {
                                Stage = "下载原版信息",
                                CurrentFile = $"下载原版客户端... {sz}",
                                Done = (int)(read / 1024),
                                Total = total > 0 ? (int)(total / 1024) : 0
                            });
                        }
                    }
                    await dst.FlushAsync(cts.Token);

                    if (expectedSize > 0 && read < expectedSize / 2)
                        throw new Exception("下载不完整");

                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { if (File.Exists(targetPath)) File.Delete(targetPath); } catch { }
                }
            }

            throw new Exception($"下载原版客户端失败：{lastEx?.Message}");
        }
    }
}