using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class NeoForgeInstaller
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        static NeoForgeInstaller()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");
        }

        // ================================================================
        // URL 构造（1.20.1 用 forge 坐标，其他用 neoforge 坐标）
        // ================================================================

        public static string GetInstallerUrl(string mcVersion, string nfVersion)
        {
            if (mcVersion == "1.20.1")
            {
                string combined = nfVersion.StartsWith(mcVersion + "-")
                    ? nfVersion : $"{mcVersion}-{nfVersion}";
                return $"https://maven.neoforged.net/releases/net/neoforged/forge/" +
                       $"{combined}/forge-{combined}-installer.jar";
            }
            return $"https://maven.neoforged.net/releases/net/neoforged/neoforge/" +
                   $"{nfVersion}/neoforge-{nfVersion}-installer.jar";
        }

        public static string GetMirrorInstallerUrl(string mcVersion, string nfVersion)
        {
            if (mcVersion == "1.20.1")
            {
                string combined = nfVersion.StartsWith(mcVersion + "-")
                    ? nfVersion : $"{mcVersion}-{nfVersion}";
                return $"https://bmclapi2.bangbang93.com/maven/net/neoforged/forge/" +
                       $"{combined}/forge-{combined}-installer.jar";
            }
            return $"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/" +
                   $"{nfVersion}/neoforge-{nfVersion}-installer.jar";
        }

        private static string GetInstallerFileName(string mcVersion, string nfVersion)
        {
            if (mcVersion == "1.20.1")
            {
                string combined = nfVersion.StartsWith(mcVersion + "-")
                    ? nfVersion : $"{mcVersion}-{nfVersion}";
                return $"forge-{combined}-installer.jar";
            }
            return $"neoforge-{nfVersion}-installer.jar";
        }

        private static void EnsureLauncherProfiles(string mcFolder)
        {
            var path = Path.Combine(mcFolder, "launcher_profiles.json");

            if (File.Exists(path))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(text) && text.Contains("\"profiles\""))
                        return;
                }
                catch { }
            }

            try
            {
                Directory.CreateDirectory(mcFolder);
                const string minimal =
                    "{\n  \"profiles\": {},\n  \"settings\": {},\n  \"version\": 3\n}\n";
                File.WriteAllText(path, minimal, new UTF8Encoding(false));
            }
            catch { }
        }

        // ================================================================
        // 下载 installer.jar
        // ================================================================

        public static async Task<string> DownloadInstallerAsync(
            string mcVersion, string nfVersion,
            IProgress<DownloadProgress>? progress = null)
        {
            var cacheDir = Path.Combine(VersionScanner.MinecraftFolder, "installers");
            Directory.CreateDirectory(cacheDir);

            var fileName = GetInstallerFileName(mcVersion, nfVersion);
            var targetPath = Path.Combine(cacheDir, fileName);

            if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 100_000)
                return targetPath;

            var urls = new[]
            {
                GetMirrorInstallerUrl(mcVersion, nfVersion),
                GetInstallerUrl(mcVersion, nfVersion)
            };

            Exception? lastEx = null;
            bool anyNotFound = false;

            foreach (var url in urls)
            {
                try
                {
                    await DownloadStreamAsync(url, targetPath, fileName, progress);
                    if (File.Exists(targetPath) && new FileInfo(targetPath).Length > 100_000)
                        return targetPath;
                    throw new Exception("下载文件过小");
                }
                catch (HttpRequestException hex) when (hex.StatusCode == HttpStatusCode.NotFound)
                {
                    anyNotFound = true; lastEx = hex;
                    try { if (File.Exists(targetPath)) File.Delete(targetPath); } catch { }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { if (File.Exists(targetPath)) File.Delete(targetPath); } catch { }
                }
            }

            if (anyNotFound)
                throw new Exception($"NeoForge {nfVersion} 的安装器不存在（404）。请选择其他版本。");

            throw new Exception($"无法下载 NeoForge 安装器\n最后错误：{lastEx?.Message}");
        }

        private static async Task DownloadStreamAsync(
            string url, string targetPath, string name,
            IProgress<DownloadProgress>? progress)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();

            long totalBytes = resp.Content.Headers.ContentLength ?? 0;
            long read = 0;

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

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
                        Stage = "下载 NeoForge 安装器",
                        CurrentFile = $"下载 {name}... {sz}",
                        Done = (int)(read / 1024),
                        Total = totalBytes > 0 ? (int)(totalBytes / 1024) : 0
                    });
                }
            }
            await dst.FlushAsync(cts.Token);
        }

        // ================================================================
        // 主流程：沙箱安装
        // ================================================================

        public static async Task InstallAsync(
            string mcVersion, string nfVersion, string targetName,
            IProgress<DownloadProgress>? progress = null)
        {
            var mcFolder = VersionScanner.MinecraftFolder;
            var targetDir = Path.Combine(mcFolder, "versions", targetName);
            var realLibs = Path.Combine(mcFolder, "libraries");

            if (Directory.Exists(targetDir) && Directory.GetFiles(targetDir).Length > 0)
                throw new Exception(
                    $"目标版本目录已存在且非空：{targetName}\n\n请返回修改游戏名称。");

            // ---------- 1. Java ----------
            progress?.Report(new DownloadProgress
            {
                Stage = "检查游戏环境",
                CurrentFile = "正在查找 Java...",
                Done = 0, Total = 100
            });

            int requiredJava = GetRequiredJavaForNeoForge(mcVersion);
            var allJava = JavaInfo.ScanAll();
            var java = JavaInfo.PickBest(allJava, requiredJava)
                ?? throw new Exception($"需要 Java {requiredJava}+ 才能运行 NeoForge 安装器");

            progress?.Report(new DownloadProgress
            {
                Stage = "检查游戏环境",
                CurrentFile = $"使用 Java {java.MajorVersion}：{java.JavaPath}",
                Done = 2, Total = 100
            });

            // ---------- 2. 下载 installer ----------
            progress?.Report(new DownloadProgress
            {
                Stage = "下载 NeoForge 安装器",
                CurrentFile = "下载 NeoForge 安装器...",
                Done = 5, Total = 100
            });

            var installerPath = await DownloadInstallerAsync(mcVersion, nfVersion, progress);

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 NeoForge 安装器",
                CurrentFile = "NeoForge 安装器下载完成",
                Done = 20, Total = 100
            });

            // ---------- 3. 创建沙箱 ----------
            var sandbox = Path.Combine(Path.GetTempPath(),
                $"nodepulse-neoforge-{Guid.NewGuid():N}");
            Directory.CreateDirectory(sandbox);

            try
            {
                EnsureLauncherProfiles(sandbox);

                Directory.CreateDirectory(realLibs);
                var sandboxLibs = Path.Combine(sandbox, "libraries");
                try
                {
                    CreateJunctionOrSymlink(sandboxLibs, realLibs);
                }
                catch
                {
                    Directory.CreateDirectory(sandboxLibs);
                }

                // 3.3 沙箱的 versions/<mcVersion>/ 里放原版 JSON + client.jar
                var sandboxVanillaDir = Path.Combine(sandbox, "versions", mcVersion);
                Directory.CreateDirectory(sandboxVanillaDir);

                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版信息",
                    CurrentFile = $"正在获取原版 {mcVersion} 信息...",
                    Done = 22, Total = 100
                });

                var list = await VersionDownloader.GetVersionListAsync();
                var entry = list.FirstOrDefault(v => v.Id == mcVersion)
                    ?? throw new Exception($"Mojang 版本清单里找不到原版 {mcVersion}");

                var vanillaJsonText = await GetStringWithFallback(entry.Url);
                File.WriteAllText(
                    Path.Combine(sandboxVanillaDir, mcVersion + ".json"),
                    vanillaJsonText, Encoding.UTF8);

                var vanillaRoot = JsonNode.Parse(vanillaJsonText)?.AsObject();
                var clientUrl = vanillaRoot?["downloads"]?["client"]?["url"]?.GetValue<string>();
                var clientSize = vanillaRoot?["downloads"]?["client"]?["size"]?.GetValue<long>() ?? 0;

                if (string.IsNullOrEmpty(clientUrl))
                    throw new Exception("原版 JSON 缺少 client.jar 下载地址");

                var sandboxClientJar = Path.Combine(sandboxVanillaDir, mcVersion + ".jar");

                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版信息",
                    CurrentFile = $"下载原版 {mcVersion} 客户端...",
                    Done = 25, Total = 100
                });

                await DownloadToFileAsync(clientUrl, sandboxClientJar, clientSize, progress);

                // 3.4 预下载 NeoForge 依赖库
                await PreDownloadNeoForgeLibrariesAsync(installerPath, mcFolder, progress);

                // ---------- 4. 在沙箱里跑 installer ----------
                progress?.Report(new DownloadProgress
                {
                    Stage = "安装 NeoForge",
                    CurrentFile = "正在沙箱中运行 NeoForge 安装器...",
                    Done = 60, Total = 100
                });

                await RunInstallerAsync(installerPath, sandbox, java.JavaPath, progress);

                // ---------- 5. 找到 installer 生成的版本目录 ----------
                var sandboxVersionsDir = Path.Combine(sandbox, "versions");
                var nfDirName = Directory.GetDirectories(sandboxVersionsDir)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n)
                                && !string.Equals(n, mcVersion, StringComparison.OrdinalIgnoreCase)
                                && !n!.StartsWith("."))
                    .OrderByDescending(n =>
                    {
                        try
                        {
                            return Directory.GetLastWriteTime(Path.Combine(sandboxVersionsDir, n!));
                        }
                        catch { return DateTime.MinValue; }
                    })
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(nfDirName))
                    throw new Exception("NeoForge installer 运行后没有生成版本目录");

                var sandboxForgeDir = Path.Combine(sandboxVersionsDir, nfDirName);

                progress?.Report(new DownloadProgress
                {
                    Stage = "配置 NeoForge 实例",
                    CurrentFile = $"正在把 {nfDirName} 搬运到目标目录...",
                    Done = 85, Total = 100
                });

                // ---------- 6. 搬运 ----------
                Directory.CreateDirectory(targetDir);

                var sandboxJson = Path.Combine(sandboxForgeDir, nfDirName + ".json");
                var targetJson = Path.Combine(targetDir, targetName + ".json");
                if (File.Exists(sandboxJson))
                    File.Copy(sandboxJson, targetJson, overwrite: true);

                var sandboxJar = Path.Combine(sandboxForgeDir, nfDirName + ".jar");
                var targetJar = Path.Combine(targetDir, targetName + ".jar");
                if (File.Exists(sandboxJar))
                    File.Copy(sandboxJar, targetJar, overwrite: true);

                foreach (var file in Directory.GetFiles(sandboxForgeDir))
                {
                    var fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Copy(file, Path.Combine(targetDir, fileName), overwrite: true); } catch { }
                }

                foreach (var dir in Directory.GetDirectories(sandboxForgeDir))
                {
                    var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                    CopyDirectoryRecursive(dir, dest);
                }

                // ---------- 7. 合并原版 JSON ----------
                progress?.Report(new DownloadProgress
                {
                    Stage = "配置 NeoForge 实例",
                    CurrentFile = "合并原版信息到版本 JSON...",
                    Done = 90, Total = 100
                });

                var sandboxVanillaJson = Path.Combine(sandboxVanillaDir, mcVersion + ".json");
                MergeVanillaIntoLoaderJson(targetDir, targetName, sandboxVanillaJson);

                if (!File.Exists(targetJar) && File.Exists(sandboxClientJar))
                {
                    try { File.Copy(sandboxClientJar, targetJar); } catch { }
                }

                // ---------- 8. 校验补全 ----------
                progress?.Report(new DownloadProgress
                {
                    Stage = "校验游戏文件",
                    CurrentFile = "校验并补全游戏资源...",
                    Done = 95, Total = 100
                });

                try
                {
                    await MinecraftLauncher.ValidateAndRepairAsync(targetName, progress);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[NeoForgeInstaller] 安装后补全失败（不影响安装，启动时会重试）：{ex.Message}");
                }

                progress?.Report(new DownloadProgress
                {
                    Stage = "已完成",
                    CurrentFile = $"NeoForge {mcVersion}-{nfVersion} 安装完成",
                    Done = 100, Total = 100
                });
            }
            finally
            {
                CleanupSandbox(sandbox);
            }
        }

        // ================================================================
        // 沙箱辅助
        // ================================================================

        private static void CreateJunctionOrSymlink(string linkPath, string targetPath)
        {
            if (Directory.Exists(linkPath)) return;

            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            else
            {
                try
                {
                    Directory.CreateSymbolicLink(linkPath, targetPath);
                }
                catch
                {
                    Directory.CreateDirectory(linkPath);
                }
            }
        }

        private static void CleanupSandbox(string sandbox)
        {
            try
            {
                var sandboxLibs = Path.Combine(sandbox, "libraries");
                if (Directory.Exists(sandboxLibs))
                {
                    try
                    {
                        var attr = File.GetAttributes(sandboxLibs);
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                            Directory.Delete(sandboxLibs, recursive: false);
                    }
                    catch { }
                }

                Directory.Delete(sandbox, recursive: true);
            }
            catch { }
        }

        private static void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (var file in Directory.GetFiles(src))
            {
                var destFile = Path.Combine(dst, Path.GetFileName(file));
                try { File.Copy(file, destFile, overwrite: true); } catch { }
            }

            foreach (var dir in Directory.GetDirectories(src))
            {
                var destDir = Path.Combine(dst, Path.GetFileName(dir));
                CopyDirectoryRecursive(dir, destDir);
            }
        }

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

        // ================================================================
        // 合并原版 JSON
        // ================================================================

        private static void MergeVanillaIntoLoaderJson(
            string targetDir, string targetName, string vanillaJsonPath)
        {
            var targetJson = Path.Combine(targetDir, targetName + ".json");
            if (!File.Exists(targetJson)) return;
            if (!File.Exists(vanillaJsonPath)) return;

            try
            {
                var loaderText = File.ReadAllText(targetJson);
                var loader = JsonNode.Parse(loaderText)?.AsObject();
                if (loader == null) return;

                var vanillaText = File.ReadAllText(vanillaJsonPath);
                var vanilla = JsonNode.Parse(vanillaText)?.AsObject();
                if (vanilla == null) return;

                var mergedLibs = new JsonArray();
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (loader["libraries"] is JsonArray loaderLibs)
                {
                    foreach (var libNode in loaderLibs)
                    {
                        var key = GetLibKey(libNode as JsonObject);
                        if (!string.IsNullOrEmpty(key)) seenKeys.Add(key);
                        mergedLibs.Add(libNode?.DeepClone());
                    }
                }

                if (vanilla["libraries"] is JsonArray vanillaLibs)
                {
                    foreach (var libNode in vanillaLibs)
                    {
                        var key = GetLibKey(libNode as JsonObject);
                        if (!string.IsNullOrEmpty(key) && !seenKeys.Add(key))
                            continue;
                        mergedLibs.Add(libNode?.DeepClone());
                    }
                }

                loader["libraries"] = mergedLibs;

                var mergedJvm = new JsonArray();
                var mergedGame = new JsonArray();

                if (vanilla["arguments"] is JsonObject vArgs)
                {
                    if (vArgs["jvm"] is JsonArray vJvm)
                        foreach (var a in vJvm) mergedJvm.Add(a?.DeepClone());
                    if (vArgs["game"] is JsonArray vGame)
                        foreach (var a in vGame) mergedGame.Add(a?.DeepClone());
                }

                if (loader["arguments"] is JsonObject lArgs)
                {
                    if (lArgs["jvm"] is JsonArray lJvm)
                        foreach (var a in lJvm) mergedJvm.Add(a?.DeepClone());
                    if (lArgs["game"] is JsonArray lGame)
                        foreach (var a in lGame) mergedGame.Add(a?.DeepClone());
                }

                loader["arguments"] = new JsonObject
                {
                    ["jvm"] = mergedJvm,
                    ["game"] = mergedGame
                };

                string[] carryFields =
                {
                    "assets", "assetIndex", "downloads", "javaVersion",
                    "minecraftArguments", "releaseTime", "time"
                };

                foreach (var field in carryFields)
                {
                    if (loader[field] == null && vanilla[field] != null)
                        loader[field] = vanilla[field]!.DeepClone();
                }

                loader.Remove("inheritsFrom");
                loader["id"] = targetName;
                if (loader["type"] == null) loader["type"] = "release";

                File.WriteAllText(targetJson,
                    loader.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeInstaller] 合并原版 JSON 失败：{ex.Message}");
            }
        }

        private static string GetLibKey(JsonObject? lib)
        {
            if (lib == null) return "";
            var name = lib["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) return "";

            var atIdx = name.IndexOf('@');
            if (atIdx >= 0) name = name.Substring(0, atIdx);

            var parts = name.Split(':');
            if (parts.Length < 2) return name;

            var classifier = parts.Length >= 4 ? parts[3] : "";
            return string.IsNullOrEmpty(classifier)
                ? $"{parts[0]}:{parts[1]}"
                : $"{parts[0]}:{parts[1]}:{classifier}";
        }

        // ================================================================
        // 预下载 NeoForge 依赖库
        // ================================================================

        private static async Task PreDownloadNeoForgeLibrariesAsync(
            string installerJarPath, string mcFolder,
            IProgress<DownloadProgress>? progress)
        {
            progress?.Report(new DownloadProgress
            {
                Stage = "安装 NeoForge",
                CurrentFile = "分析 NeoForge 安装器依赖...",
                Done = 50, Total = 100
            });

            var tasks = new List<DownloadTask>();
            string libRoot = Path.Combine(mcFolder, "libraries");

            try
            {
                using var zip = ZipFile.OpenRead(installerJarPath);
                var profileEntry = zip.GetEntry("install_profile.json");
                if (profileEntry == null)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Stage = "安装 NeoForge",
                        CurrentFile = "installer 缺少 install_profile.json，跳过预下载",
                        Done = 55, Total = 100
                    });
                    return;
                }

                string profileJson;
                using (var sr = new StreamReader(profileEntry.Open()))
                    profileJson = await sr.ReadToEndAsync();

                var profileRoot = JsonNode.Parse(profileJson)?.AsObject();

                var libs = profileRoot?["libraries"]?.AsArray();
                if (libs != null)
                    foreach (var libNode in libs)
                        AddLibraryTask(libNode?.AsObject(), libRoot, tasks);

                var dataNode = profileRoot?["data"]?.AsObject();
                if (dataNode != null)
                {
                    foreach (var kv in dataNode)
                    {
                        var val = kv.Value?.AsObject();
                        AddDataRefTask(val?["client"]?.GetValue<string>(), libRoot, tasks);
                        AddDataRefTask(val?["server"]?.GetValue<string>(), libRoot, tasks);
                    }
                }

                var versionEntry = zip.GetEntry("version.json");
                if (versionEntry != null)
                {
                    string versionJson;
                    using (var sr = new StreamReader(versionEntry.Open()))
                        versionJson = await sr.ReadToEndAsync();

                    var versionRoot = JsonNode.Parse(versionJson)?.AsObject();
                    var vLibs = versionRoot?["libraries"]?.AsArray();
                    if (vLibs != null)
                        foreach (var libNode in vLibs)
                            AddLibraryTask(libNode?.AsObject(), libRoot, tasks);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NeoForgeInstaller] 预下载分析失败: {ex.Message}");
                return;
            }

            var unique = tasks
                .GroupBy(t => t.Target, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var needDownload = new List<DownloadTask>();
            foreach (var t in unique)
            {
                if (File.Exists(t.Target))
                {
                    try
                    {
                        var fi = new FileInfo(t.Target);
                        long minSize = t.MinSize > 0 ? t.MinSize : 100;
                        if (fi.Length >= minSize) continue;
                    }
                    catch { }
                }
                needDownload.Add(t);
            }

            if (needDownload.Count == 0)
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "安装 NeoForge",
                    CurrentFile = "所有 NeoForge 库已就绪",
                    Done = 60, Total = 100
                });
                return;
            }

            progress?.Report(new DownloadProgress
            {
                Stage = "安装 NeoForge",
                CurrentFile = $"预下载 NeoForge 库 {needDownload.Count}/{unique.Count}...",
                Done = 55, Total = 100
            });

            var mappedProgress = new Progress<DownloadProgress>(p =>
            {
                double innerPct = p.Total > 0 ? (double)p.Done / p.Total * 100 : 0;
                if (innerPct > 100) innerPct = 100;
                double mapped = 55 + innerPct * 5.0 / 100.0;
                progress?.Report(new DownloadProgress
                {
                    Stage = "安装 NeoForge",
                    CurrentFile = $"[预下载] {p.CurrentFile}",
                    Done = (int)mapped, Total = 100
                });
            });

            await VersionDownloader.DownloadTasksAsync(
                needDownload, mappedProgress, "neoforge-predownload");

            progress?.Report(new DownloadProgress
            {
                Stage = "安装 NeoForge",
                CurrentFile = $"NeoForge 库预下载完成（{needDownload.Count} 个文件）",
                Done = 60, Total = 100
            });
        }

        private static void AddLibraryTask(
            JsonObject? lib, string libRoot, List<DownloadTask> tasks)
        {
            if (lib == null) return;
            var name = lib["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) return;

            var downloads = lib["downloads"]?.AsObject();
            var artifact = downloads?["artifact"]?.AsObject();

            string? path = artifact?["path"]?.GetValue<string>();
            string? url = artifact?["url"]?.GetValue<string>();
            long size = artifact?["size"]?.GetValue<long>() ?? 0;

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url))
            {
                var (inferredPath, inferredUrl) = BuildMavenPathFromName(name);
                if (!string.IsNullOrEmpty(inferredPath))
                {
                    path = inferredPath;
                    url = inferredUrl;
                    size = 0;
                }
            }

            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url))
                return;

            var target = Path.Combine(libRoot,
                path.Replace('/', Path.DirectorySeparatorChar));

            tasks.Add(new DownloadTask
            {
                Url = url,
                Target = target,
                Name = Path.GetFileName(path),
                MinSize = size > 0 ? size * 9 / 10 : 100
            });
        }

        private static void AddDataRefTask(
            string? refStr, string libRoot, List<DownloadTask> tasks)
        {
            if (string.IsNullOrEmpty(refStr)) return;
            if (refStr.Length < 2) return;
            if (!refStr.StartsWith("[") || !refStr.EndsWith("]")) return;

            string mavenRef = refStr.Substring(1, refStr.Length - 2);
            var (path, url) = BuildMavenPathFromName(mavenRef);
            if (string.IsNullOrEmpty(path)) return;

            var target = Path.Combine(libRoot,
                path.Replace('/', Path.DirectorySeparatorChar));

            tasks.Add(new DownloadTask
            {
                Url = url,
                Target = target,
                Name = Path.GetFileName(path),
                MinSize = 100
            });
        }

        private static (string path, string url) BuildMavenPathFromName(string mavenName)
        {
            try
            {
                string name = mavenName;
                string ext = "jar";

                int at = name.IndexOf('@');
                if (at >= 0)
                {
                    ext = name.Substring(at + 1);
                    name = name.Substring(0, at);
                }

                var parts = name.Split(':');
                if (parts.Length < 3) return ("", "");

                string group = parts[0];
                string artifact = parts[1];
                string version = parts[2];
                string classifier = parts.Length >= 4 ? parts[3] : "";

                string groupPath = group.Replace('.', '/');
                string fileName = string.IsNullOrEmpty(classifier)
                    ? $"{artifact}-{version}.{ext}"
                    : $"{artifact}-{version}-{classifier}.{ext}";

                string path = $"{groupPath}/{artifact}/{version}/{fileName}";
                string url = "https://bmclapi2.bangbang93.com/maven/" + path;

                return (path, url);
            }
            catch { return ("", ""); }
        }

        // ================================================================
        // 执行 installer
        // ================================================================

        public static async Task<string> RunInstallerAsync(
            string installerJarPath, string mcFolder, string javaPath,
            IProgress<DownloadProgress>? progress = null)
        {
            EnsureLauncherProfiles(mcFolder);

            progress?.Report(new DownloadProgress
            {
                Stage = "安装 NeoForge",
                CurrentFile = "正在执行 NeoForge 安装器..."
            });

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                WorkingDirectory = mcFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-Dsun.net.client.defaultConnectTimeout=15000");
            psi.ArgumentList.Add("-Dsun.net.client.defaultReadTimeout=60000");
            psi.ArgumentList.Add("-Djava.net.preferIPv4Stack=true");
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(installerJarPath);
            psi.ArgumentList.Add("--installClient");
            psi.ArgumentList.Add(mcFolder);

            var output = new StringBuilder();
            var error = new StringBuilder();

            using var proc = new Process { StartInfo = psi };

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    progress?.Report(new DownloadProgress
                    {
                        Stage = "安装 NeoForge",
                        CurrentFile = $"NeoForge 安装：{e.Data}"
                    });
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) error.AppendLine(e.Data);
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var timeout = Task.Delay(TimeSpan.FromMinutes(10));
            var wait = Task.Run(() => proc.WaitForExit());
            var done = await Task.WhenAny(wait, timeout);

            if (done == timeout && !proc.HasExited)
            {
                try { proc.Kill(true); } catch { }
                throw new Exception("NeoForge 安装器执行超时（10 分钟）");
            }

            string fullLog = output.ToString() + Environment.NewLine + error.ToString();

            if (proc.ExitCode != 0)
                throw new Exception($"NeoForge 安装器退出码 {proc.ExitCode}\n{fullLog}");

            if (!fullLog.Contains("SUCCESSFUL", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"NeoForge 安装器未报告成功\n{fullLog}");

            return fullLog;
        }

        private static int GetRequiredJavaForNeoForge(string mcVersion)
        {
            return JavaRequirementHelper.GetRequiredJavaFor(mcVersion);
        }
    }
}