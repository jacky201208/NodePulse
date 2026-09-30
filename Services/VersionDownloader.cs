using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using NodePulse.Models;
using SharpCompress.Compressors.Xz;

namespace NodePulse.Services
{
    public static class VersionDownloader
    {
        public static bool UseMirror { get; set; } = false;

        private static int _cachedThreadCount = -1;
        private static DateTime _threadCountCacheTime = DateTime.MinValue;

        public static int ThreadCount
        {
            get
            {
                var now = DateTime.UtcNow;
                if (_cachedThreadCount > 0 &&
                    (now - _threadCountCacheTime).TotalSeconds < 5)
                    return _cachedThreadCount;

                try
                {
                    var n = GlobalSettings.Load().DownloadThreadCount;
                    if (n < 1) n = 1;
                    if (n > 256) n = 256;

                    _cachedThreadCount = n;
                    _threadCountCacheTime = now;
                    return n;
                }
                catch { return 32; }
            }
        }

        public static int RetryCount
        {
            get
            {
                try
                {
                    var n = GlobalSettings.Load().DownloadRetryCount;
                    return n < 1 ? 1 : (n > 5 ? 5 : n);
                }
                catch { return 3; }
            }
        }

        private const int BufferSize = 128 * 1024;
        private const long ChunkedDownloadThreshold = 1 * 1024 * 1024;
        private const long ChunkedMinChunkSize = 512 * 1024;
        private const int ChunkedMaxChunks = 16;
        private const int ChunkedMaxConcurrency = 8;
        private const int ChunkedChunkRetry = 3;

        // ================================================================
        // DNS + 连接
        // ================================================================

        private class DnsEntry
        {
            public IPAddress[] Addresses = Array.Empty<IPAddress>();
            public DateTime ExpiresAt;
        }

        private static readonly ConcurrentDictionary<string, DnsEntry> _dnsCache = new();
        private static readonly TimeSpan DnsTtl = TimeSpan.FromMinutes(5);

        private static async ValueTask<Stream> ConnectCallback(
            SocketsHttpConnectionContext context,
            CancellationToken ct)
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            IPAddress[] ips;
            if (_dnsCache.TryGetValue(host, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
            {
                ips = entry.Addresses;
            }
            else
            {
                ips = await Dns.GetHostAddressesAsync(host, ct);
                _dnsCache[host] = new DnsEntry
                {
                    Addresses = ips,
                    ExpiresAt = DateTime.UtcNow + DnsTtl
                };
            }

            Exception? lastEx = null;
            foreach (var ip in ips)
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try { socket.SetSocketOption(SocketOptionLevel.Tcp, (SocketOptionName)15, 1); } catch { }
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(ip, port), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    try { socket.Dispose(); } catch { }
                }
            }
            throw lastEx ?? new Exception($"无法连接到 {host}:{port}");
        }

        private static readonly HttpClient _http = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip |
                                         DecompressionMethods.Deflate |
                                         DecompressionMethods.Brotli,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
                MaxConnectionsPerServer = 512,
                ConnectCallback = ConnectCallback,
                EnableMultipleHttp2Connections = true,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                Expect100ContinueTimeout = TimeSpan.Zero
            };

            var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NodePulse/1.0");
            return client;
        }

        private const string OfficialManifest =
            "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

        private const string MirrorManifest =
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json";

        private const string ManifestCacheCategory = "mojang-manifest";
        private const string ManifestCacheKey = "version_manifest_v2";
        private static readonly TimeSpan ManifestCacheTtl = TimeSpan.FromHours(1);

        private static readonly Queue<bool> _recentResults = new();
        private static readonly object _adaptiveLock = new();
        private static DateTime _pauseUntil = DateTime.MinValue;

        private static DateTime _lastGlobalByteTime = DateTime.UtcNow;
        private static bool _stalled = false;

        static VersionDownloader()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try { await Task.Delay(2000); CheckStall(); } catch { }
                }
            });
        }

        private static void CheckStall()
        {
            var idleSec = (DateTime.UtcNow - _lastGlobalByteTime).TotalSeconds;
            lock (_adaptiveLock)
            {
                if (idleSec > 10 && !_stalled) _stalled = true;
                else if (idleSec <= 2 && _stalled) _stalled = false;
            }
        }

        private static void ReportBytesTransferred(long delta)
        {
            if (delta <= 0) return;
            _lastGlobalByteTime = DateTime.UtcNow;
        }

        private static void ReportRequestResult(bool success)
        {
            lock (_adaptiveLock)
            {
                _recentResults.Enqueue(success);
                while (_recentResults.Count > 50) _recentResults.Dequeue();

                if (_recentResults.Count < 10) return;

                double failRate = _recentResults.Count(x => !x) / (double)_recentResults.Count;
                if (failRate > 0.30)
                    _pauseUntil = DateTime.UtcNow.AddSeconds(3);
            }
        }

        private static async Task ThrottleAsync()
        {
            var pauseUntil = _pauseUntil;
            if (pauseUntil > DateTime.UtcNow)
            {
                var wait = pauseUntil - DateTime.UtcNow;
                if (wait.TotalMilliseconds > 0)
                    await Task.Delay(wait);
            }
        }

        // ================================================================
        // 版本清单
        // ================================================================

        public static async Task<List<VersionInfo>> GetVersionListAsync()
        {
            try
            {
                var cached = CacheManager.Read(
                    ManifestCacheCategory, ManifestCacheKey, ".json", ManifestCacheTtl);

                if (cached != null && cached.Length > 0)
                {
                    try
                    {
                        var cachedJson = Encoding.UTF8.GetString(cached);
                        var cachedList = ParseManifest(cachedJson);
                        if (cachedList.Count > 0)
                            return cachedList;
                    }
                    catch
                    {
                        try
                        {
                            var p = CacheManager.GetFilePath(
                                ManifestCacheCategory, ManifestCacheKey, ".json");
                            if (File.Exists(p)) File.Delete(p);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            string json;
            try
            {
                json = await _http.GetStringAsync(OfficialManifest);
            }
            catch
            {
                json = await _http.GetStringAsync(MirrorManifest);
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                CacheManager.Write(ManifestCacheCategory, ManifestCacheKey, bytes, ".json");
            }
            catch { }

            return ParseManifest(json);
        }

        private static List<VersionInfo> ParseManifest(string json)
        {
            var root = JsonNode.Parse(json)!.AsObject();
            var result = new List<VersionInfo>();
            var versions = root["versions"]?.AsArray();
            if (versions is null) return result;

            foreach (var v in versions)
            {
                var obj = v!.AsObject();
                result.Add(new VersionInfo
                {
                    Id = obj["id"]?.GetValue<string>() ?? "",
                    Type = obj["type"]?.GetValue<string>() ?? "",
                    Url = obj["url"]?.GetValue<string>() ?? "",
                    ReleaseTime = obj["releaseTime"]?.GetValue<string>() ?? ""
                });
            }
            return result;
        }

        public static async Task<string> DownloadVersionJsonAsync(VersionInfo version)
        {
            var json = await GetStringWithFallback(version.Url);

            var targetDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions", version.Id);
            Directory.CreateDirectory(targetDir);

            var targetFile = Path.Combine(targetDir, version.Id + ".json");
            File.WriteAllText(targetFile, json, System.Text.Encoding.UTF8);

            return json;
        }

        public static string ToMirrorUrl(string officialUrl)
        {
            if (string.IsNullOrEmpty(officialUrl)) return officialUrl;

            return officialUrl
                .Replace("https://piston-meta.mojang.com/", "https://bmclapi2.bangbang93.com/")
                .Replace("https://piston-data.mojang.com/", "https://bmclapi2.bangbang93.com/")
                .Replace("https://launchermeta.mojang.com/", "https://bmclapi2.bangbang93.com/")
                .Replace("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/")
                .Replace("https://resources.download.minecraft.net/", "https://bmclapi2.bangbang93.com/assets/")
                .Replace("https://maven.minecraftforge.net/", "https://bmclapi2.bangbang93.com/maven/")
                .Replace("https://maven.neoforged.net/releases/", "https://bmclapi2.bangbang93.com/maven/")
                .Replace("https://maven.fabricmc.net/", "https://bmclapi2.bangbang93.com/maven/")
                .Replace("https://files.minecraftforge.net/maven/", "https://bmclapi2.bangbang93.com/maven/");
        }

        private static async Task<string> GetStringWithFallback(string officialUrl)
        {
            try
            {
                return await _http.GetStringAsync(officialUrl);
            }
            catch
            {
                if (!officialUrl.Contains("bmclapi2.bangbang93.com"))
                {
                    var mirror = ToMirrorUrl(officialUrl);
                    if (mirror != officialUrl)
                        return await _http.GetStringAsync(mirror);
                }
                throw;
            }
        }

        // ================================================================
        // 校验是否是干净的原版 JSON
        // ================================================================

        private static bool IsCleanVanillaJson(string json, string expectedId)
        {
            try
            {
                var root = JsonNode.Parse(json)?.AsObject();
                if (root == null) return false;

                var id = root["id"]?.GetValue<string>();
                if (!string.Equals(id, expectedId, StringComparison.OrdinalIgnoreCase))
                    return false;

                var inheritsFrom = root["inheritsFrom"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(inheritsFrom))
                    return false;

                var assetUrl = root["assetIndex"]?["url"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(assetUrl))
                    return true;

                var mainClass = root["mainClass"]?.GetValue<string>() ?? "";
                if (mainClass.Equals("net.minecraft.client.main.Main",
                        StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
            catch { return false; }
        }

        // ================================================================
        // 阶段 2
        // ================================================================

        public static async Task DownloadFullVersionAsync(
            VersionInfo version,
            IProgress<DownloadProgress>? progress)
        {
            var chain = await CollectInheritChainAsync(version);

            foreach (var v in chain)
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版 Json 文件",
                    CurrentFile = $"版本 {v.Id} 元数据"
                });
                await DownloadSingleVersionAsync(v, progress);
            }
        }

        private static async Task<List<VersionInfo>> CollectInheritChainAsync(VersionInfo version)
        {
            var mcFolder = VersionScanner.MinecraftFolder;
            var chain = new List<VersionInfo>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var current = version;
            while (current != null && visited.Add(current.Id))
            {
                chain.Insert(0, current);

                var versionDir = Path.Combine(mcFolder, "versions", current.Id);
                var jsonPath = Path.Combine(versionDir, current.Id + ".json");

                string? inheritsFrom = null;
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        var root = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();
                        inheritsFrom = root["inheritsFrom"]?.GetValue<string>();
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        var json = await GetStringWithFallback(current.Url);
                        Directory.CreateDirectory(versionDir);
                        File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
                        var root = JsonNode.Parse(json)!.AsObject();
                        inheritsFrom = root["inheritsFrom"]?.GetValue<string>();
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(inheritsFrom)) break;

                var list = await GetVersionListAsync();
                var parent = list.FirstOrDefault(x => x.Id == inheritsFrom);
                if (parent == null) break;

                current = parent;
            }

            return chain;
        }

        private static async Task DownloadSingleVersionAsync(
            VersionInfo version,
            IProgress<DownloadProgress>? progress)
        {
            var mcFolder = VersionScanner.MinecraftFolder;
            var versionDir = Path.Combine(mcFolder, "versions", version.Id);
            var jsonPath = Path.Combine(versionDir, version.Id + ".json");

            string json;
            if (File.Exists(jsonPath))
            {
                json = File.ReadAllText(jsonPath);

                if (!IsCleanVanillaJson(json, version.Id))
                {
                    try { File.Delete(jsonPath); } catch { }
                    progress?.Report(new DownloadProgress
                    {
                        Stage = "下载原版 Json 文件",
                        CurrentFile = $"{version.Id} JSON 无效，重新下载"
                    });
                    json = await DownloadVersionJsonAsync(version);
                }
            }
            else
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版 Json 文件",
                    CurrentFile = $"{version.Id} 版本 JSON"
                });
                json = await DownloadVersionJsonAsync(version);
            }

            var root = JsonNode.Parse(json)!.AsObject();
            var tasks = new List<DownloadTask>();

            var clientUrl = root["downloads"]?["client"]?["url"]?.GetValue<string>();
            var clientSize = root["downloads"]?["client"]?["size"]?.GetValue<long>() ?? 0;
            if (!string.IsNullOrEmpty(clientUrl))
            {
                var clientJar = Path.Combine(versionDir, version.Id + ".jar");
                tasks.Add(new DownloadTask
                {
                    Url = clientUrl,
                    Target = clientJar,
                    Name = $"{version.Id} 客户端 jar",
                    MinSize = clientSize > 0 ? clientSize / 2 : 1024 * 1024
                });
            }

            if (root["libraries"] is JsonArray libs)
            {
                foreach (var libNode in libs)
                {
                    var lib = libNode!.AsObject();
                    if (!RulesAllow(lib["rules"] as JsonArray)) continue;

                    var downloads = lib["downloads"]?.AsObject();
                    var libName = lib["name"]?.GetValue<string>() ?? "";

                    if (downloads != null)
                    {
                        var artifact = downloads["artifact"]?.AsObject();
                        var libPath = artifact?["path"]?.GetValue<string>();
                        var libUrl = artifact?["url"]?.GetValue<string>();
                        var libSize = artifact?["size"]?.GetValue<long>() ?? 0;

                        if (!string.IsNullOrEmpty(libPath) &&
                            !string.IsNullOrEmpty(libUrl) &&
                            libSize > 100)
                        {
                            var fullLib = Path.Combine(mcFolder, "libraries",
                                libPath.Replace('/', Path.DirectorySeparatorChar));
                            tasks.Add(new DownloadTask
                            {
                                Url = libUrl,
                                Target = fullLib,
                                Name = Path.GetFileName(libPath),
                                MinSize = libSize > 0 ? libSize * 9 / 10 : 1024
                            });
                        }

                        var natives = lib["natives"]?.AsObject();
                        var nativeKey = natives?[PlatformHelper.NativesKey]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(nativeKey))
                        {
                            var classifiers = downloads["classifiers"]?.AsObject();
                            var nativeArtifact = classifiers?[nativeKey]?.AsObject();
                            var nativePath = nativeArtifact?["path"]?.GetValue<string>();
                            var nativeUrl = nativeArtifact?["url"]?.GetValue<string>();
                            var nativeSize = nativeArtifact?["size"]?.GetValue<long>() ?? 0;
                            if (!string.IsNullOrEmpty(nativePath) &&
                                !string.IsNullOrEmpty(nativeUrl))
                            {
                                var fullNative = Path.Combine(mcFolder, "libraries",
                                    nativePath.Replace('/', Path.DirectorySeparatorChar));
                                tasks.Add(new DownloadTask
                                {
                                    Url = nativeUrl,
                                    Target = fullNative,
                                    Name = Path.GetFileName(nativePath),
                                    MinSize = nativeSize > 0 ? nativeSize * 9 / 10 : 1024
                                });
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(libName))
                    {
                        var mavenUrl = lib["url"]?.GetValue<string>();
                        var (path, url) = BuildMavenPath(libName, mavenUrl);

                        if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(url))
                        {
                            var fullLib = Path.Combine(mcFolder, "libraries",
                                path.Replace('/', Path.DirectorySeparatorChar));

                            long minSize = 50 * 1024;
                            var lowerName = libName.ToLowerInvariant();
                            if (lowerName.Contains("intermediary") ||
                                lowerName.Contains("fabric-loader") ||
                                lowerName.Contains("sponge-mixin") ||
                                lowerName.Contains("asm"))
                            {
                                minSize = 20 * 1024;
                            }

                            tasks.Add(new DownloadTask
                            {
                                Url = url,
                                Target = fullLib,
                                Name = Path.GetFileName(path),
                                MinSize = minSize
                            });
                        }
                    }
                }
            }

            var uniqueTasks = tasks
                .GroupBy(t => t.Target, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            progress?.Report(new DownloadProgress
            {
                Stage = "下载原版支持库文件",
                Total = uniqueTasks.Count
            });
            await RunDownloadTasksAsync(uniqueTasks, ThreadCount, progress, "libraries");
        }

        private static (string path, string url) BuildMavenPath(string mavenName, string? baseUrl)
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

                string baseClean = string.IsNullOrEmpty(baseUrl)
                    ? "https://libraries.minecraft.net/"
                    : baseUrl.TrimEnd('/') + "/";

                return (path, baseClean + path);
            }
            catch { return ("", ""); }
        }

        // ================================================================
        // 阶段 3：assets
        // ================================================================

        public static async Task DownloadAssetsAsync(
            VersionInfo version,
            IProgress<DownloadProgress>? progress)
        {
            var mcFolder = VersionScanner.MinecraftFolder;

            string? assetIndexUrl = null;
            string? assetIndexId = null;

            string currentId = version.Id;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(currentId) && visited.Add(currentId))
            {
                var versionDir = Path.Combine(mcFolder, "versions", currentId);
                var jsonPath = Path.Combine(versionDir, currentId + ".json");
                if (!File.Exists(jsonPath)) break;

                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(jsonPath))!.AsObject();

                    var url = root["assetIndex"]?["url"]?.GetValue<string>();
                    var id = root["assetIndex"]?["id"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(id))
                    {
                        assetIndexUrl = url;
                        assetIndexId = id;
                        break;
                    }

                    currentId = root["inheritsFrom"]?.GetValue<string>() ?? "";
                }
                catch { break; }
            }

            if (string.IsNullOrEmpty(assetIndexUrl) || string.IsNullOrEmpty(assetIndexId))
                throw new Exception($"版本 {version.Id} 及其父版本都找不到 assetIndex 信息");

            var assetsDir = Path.Combine(mcFolder, "assets");
            var indexesDir = Path.Combine(assetsDir, "indexes");
            Directory.CreateDirectory(indexesDir);

            var indexFile = Path.Combine(indexesDir, assetIndexId + ".json");
            string indexJson;
            if (File.Exists(indexFile))
            {
                indexJson = File.ReadAllText(indexFile);
            }
            else
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版资源文件",
                    CurrentFile = "assets 索引"
                });
                indexJson = await GetStringWithFallback(assetIndexUrl);
                File.WriteAllText(indexFile, indexJson, System.Text.Encoding.UTF8);
            }

            var indexRoot = JsonNode.Parse(indexJson)!.AsObject();
            var objects = indexRoot["objects"]?.AsObject();
            if (objects is null) return;

            var tasks = new List<DownloadTask>();
            foreach (var kv in objects)
            {
                var obj = kv.Value!.AsObject();
                var hash = obj["hash"]?.GetValue<string>();
                var size = obj["size"]?.GetValue<long>() ?? 0;
                if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;

                var subDir = hash.Substring(0, 2);
                var target = Path.Combine(assetsDir, "objects", subDir, hash);
                var url = $"https://resources.download.minecraft.net/{subDir}/{hash}";

                tasks.Add(new DownloadTask
                {
                    Url = url,
                    Target = target,
                    Name = hash,
                    MinSize = size > 0 ? size : 1
                });
            }

            var uniqueTasks = tasks
                .GroupBy(t => t.Target, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            progress?.Report(new DownloadProgress
            {
                Stage = "下载原版资源文件",
                Total = uniqueTasks.Count
            });
            await RunDownloadTasksAsync(uniqueTasks, ThreadCount, progress, "assets");
        }

        // ================================================================
        // 阶段 4：Java 运行时
        // ================================================================

        public static async Task DownloadJavaRuntimeAsync(
            int majorVersion,
            IProgress<DownloadProgress>? progress)
        {
            var mcFolder = VersionScanner.MinecraftFolder;
            var runtimeDir = Path.Combine(mcFolder, "runtime");
            Directory.CreateDirectory(runtimeDir);

            const string javaManifestUrl =
                "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 Java 运行时",
                CurrentFile = "Java 运行时清单"
            });

            var manifestJson = await GetStringWithFallback(javaManifestUrl);
            var manifest = JsonNode.Parse(manifestJson)!.AsObject();

            var platformKey = PlatformHelper.JavaRuntimePlatformKey;
            var platformObj = manifest[platformKey]?.AsObject();
            if (platformObj is null)
                throw new Exception($"Mojang 清单里没有 {platformKey} 平台");

            var candidates = majorVersion switch
            {
                <= 8  => new[] { "jre-legacy" },
                <= 16 => new[] { "java-runtime-alpha", "java-runtime-beta", "java-runtime-gamma" },
                <= 17 => new[] { "java-runtime-gamma", "java-runtime-gamma-snapshot" },
                <= 21 => new[] { "java-runtime-delta", "java-runtime-epsilon", "java-runtime-gamma" },
                _     => new[] { "java-runtime-epsilon", "java-runtime-delta", "java-runtime-gamma" }
            };

            JsonArray? componentArr = null;
            string? chosenComponent = null;

            foreach (var c in candidates)
            {
                if (platformObj[c] is JsonArray arr && arr.Count > 0)
                {
                    componentArr = arr;
                    chosenComponent = c;
                    break;
                }
            }

            if (componentArr is null || chosenComponent is null)
                throw new Exception($"Mojang 清单里找不到 Java {majorVersion} 的 {platformKey} 版本");

            var firstEntry = componentArr[0]!.AsObject();
            var manifestEntryUrl = firstEntry["manifest"]?["url"]?.GetValue<string>();
            if (string.IsNullOrEmpty(manifestEntryUrl))
                throw new Exception("Java 清单里没有 manifest.url");

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 Java 运行时",
                CurrentFile = "Java 运行时文件清单"
            });

            var runtimeJson = await GetStringWithFallback(manifestEntryUrl);
            var runtimeRoot = JsonNode.Parse(runtimeJson)!.AsObject();
            var files = runtimeRoot["files"]?.AsObject();
            if (files is null) throw new Exception("Java 运行时文件清单为空");

            var targetDir = Path.Combine(runtimeDir, chosenComponent);
            var tasks = new List<DownloadTask>();

            foreach (var kv in files)
            {
                var relPath = kv.Key;
                var fileInfo = kv.Value!.AsObject();
                var type = fileInfo["type"]?.GetValue<string>();

                if (type == "directory")
                {
                    Directory.CreateDirectory(Path.Combine(targetDir,
                        relPath.Replace('/', Path.DirectorySeparatorChar)));
                    continue;
                }
                if (type == "link") continue;

                var downloads = fileInfo["downloads"]?.AsObject();
                var raw = downloads?["raw"]?.AsObject();
                var url = raw?["url"]?.GetValue<string>();
                var size = raw?["size"]?.GetValue<long>() ?? 0;
                if (string.IsNullOrEmpty(url)) continue;

                var target = Path.Combine(targetDir,
                    relPath.Replace('/', Path.DirectorySeparatorChar));
                tasks.Add(new DownloadTask
                {
                    Url = url,
                    Target = target,
                    Name = Path.GetFileName(relPath),
                    MinSize = size > 0 ? size : 1
                });
            }

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 Java 运行时",
                Total = tasks.Count
            });
            await RunDownloadTasksAsync(tasks, Math.Min(ThreadCount, 32), progress, "java-runtime");

            progress?.Report(new DownloadProgress
            {
                Stage = "下载 Java 运行时",
                CurrentFile = "解压 Java 运行时文件..."
            });
            foreach (var t in tasks)
            {
                if (t.Target.EndsWith(".pack", StringComparison.OrdinalIgnoreCase))
                {
                    var realTarget = t.Target.Substring(0, t.Target.Length - 5);
                    try
                    {
                        ExtractXz(t.Target, realTarget);
                        File.Delete(t.Target);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"解压失败 {t.Target}: {ex.Message}");
                    }
                }
            }

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    var binDir = Path.Combine(targetDir, "bin");
                    if (Directory.Exists(binDir))
                    {
                        foreach (var f in Directory.GetFiles(binDir))
                        {
                            try
                            {
                                var mode = File.GetUnixFileMode(f);
                                File.SetUnixFileMode(f,
                                    mode | UnixFileMode.UserExecute |
                                    UnixFileMode.GroupExecute |
                                    UnixFileMode.OtherExecute);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static void ExtractXz(string xzFile, string outFile)
        {
            var dir = Path.GetDirectoryName(outFile);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            using var inStream = File.OpenRead(xzFile);
            using var xzStream = new XZStream(inStream);
            using var outStream = File.Create(outFile);
            xzStream.CopyTo(outStream);
        }

        public static async Task DownloadEverythingAsync(
            VersionInfo version,
            IProgress<DownloadProgress>? progress)
        {
            _lastGlobalByteTime = DateTime.UtcNow;
            _stalled = false;
            lock (_adaptiveLock)
            {
                _pauseUntil = DateTime.MinValue;
                _recentResults.Clear();
            }

            try
            {
                var failedFile = Path.Combine(AppContext.BaseDirectory, "failed_downloads.txt");
                if (File.Exists(failedFile))
                    File.Delete(failedFile);
            }
            catch { }

            var mcFolder = VersionScanner.MinecraftFolder;
            var versionDir = Path.Combine(mcFolder, "versions", version.Id);
            var jsonPath = Path.Combine(versionDir, version.Id + ".json");

            string json;
            if (File.Exists(jsonPath))
            {
                json = File.ReadAllText(jsonPath);

                if (!IsCleanVanillaJson(json, version.Id))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[VersionDownloader] {version.Id} 的 JSON 不是干净的原版，重新下载");

                    try { File.Delete(jsonPath); } catch { }

                    progress?.Report(new DownloadProgress
                    {
                        Stage = "下载原版 Json 文件",
                        CurrentFile = $"{version.Id} JSON 需要刷新，重新下载..."
                    });

                    json = await DownloadVersionJsonAsync(version);
                }
            }
            else
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "下载原版 Json 文件",
                    CurrentFile = $"{version.Id} JSON 下载..."
                });
                json = await DownloadVersionJsonAsync(version);
            }

            var root = JsonNode.Parse(json)!.AsObject();
            int javaMajor = root["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 8;

            bool hasSuitableJava = false;
            try
            {
                var allJava = JavaInfo.ScanAll();
                hasSuitableJava = allJava.Any(j => j.MajorVersion >= javaMajor);
            }
            catch { }

            if (hasSuitableJava)
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "检查游戏环境",
                    CurrentFile = $"已检测到 Java {javaMajor}+，跳过 Java 运行时下载"
                });
            }
            else
            {
                try
                {
                    await DownloadJavaRuntimeAsync(javaMajor, progress);
                }
                catch (Exception ex)
                {
                    progress?.Report(new DownloadProgress
                    {
                        Stage = "下载 Java 运行时",
                        CurrentFile = "Java 下载失败：" + ex.Message
                    });
                }
            }

            await DownloadFullVersionAsync(version, progress);
            await DownloadAssetsAsync(version, progress);
        }

        public static async Task DownloadTasksAsync(
            List<DownloadTask> tasks,
            IProgress<DownloadProgress>? progress = null,
            string stageName = "validate")
        {
            await RunDownloadTasksAsync(tasks, ThreadCount, progress, stageName);
        }

        // ================================================================
        // 批量下载
        // ================================================================

        private const int TotalRounds = 4;

        private static async Task RunDownloadTasksAsync(
            List<DownloadTask> uniqueTasks,
            int concurrency,
            IProgress<DownloadProgress>? progress,
            string stageName)
        {
            var currentTasks = uniqueTasks;

            for (int round = 0; round < TotalRounds; round++)
            {
                if (round > 0)
                {
                    int waitSec = round * 5;
                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = $"[{stageName}] 第 {round} 轮重试 {currentTasks.Count} 个文件，等待 {waitSec} 秒..."
                    });
                    await Task.Delay(waitSec * 1000);
                }

                var failedTasks = await RunDownloadRoundAsync(
                    currentTasks, concurrency, progress, stageName, round);

                if (failedTasks.Count == 0)
                    return;

                currentTasks = failedTasks;
            }

            try
            {
                var failedFile = Path.Combine(AppContext.BaseDirectory, "failed_downloads.txt");
                var lines = new List<string>
                {
                    $"",
                    $"=== {DateTime.Now} [{stageName}] 4 轮重试后仍失败 {currentTasks.Count} 个文件 ==="
                };
                foreach (var t in currentTasks)
                {
                    lines.Add($"{t.Name}|{t.Url}|重试 4 轮仍失败");
                }
                File.AppendAllLines(failedFile, lines);
            }
            catch { }

            progress?.Report(new DownloadProgress
            {
                CurrentFile = $"⚠ [{stageName}] {currentTasks.Count} 个文件重试 4 轮仍失败"
            });
        }

        private static async Task<List<DownloadTask>> RunDownloadRoundAsync(
            List<DownloadTask> tasks,
            int concurrency,
            IProgress<DownloadProgress>? progress,
            string stageName,
            int round)
        {
            var done = 0;
            var failedTasks = new List<DownloadTask>();
            var semaphore = new SemaphoreSlim(concurrency);
            var lockObj = new object();

            var activeTasks = new Dictionary<string, DownloadTaskProgress>();
            long totalDownloadedBytes = 0;

            void ReportProgress(string? currentFile = null)
            {
                lock (lockObj)
                {
                    var snapshot = new List<DownloadTaskProgress>();
                    foreach (var t in activeTasks.Values)
                    {
                        snapshot.Add(new DownloadTaskProgress
                        {
                            Key = t.Key,
                            Name = t.Name,
                            Url = t.Url,
                            DownloadedBytes = t.DownloadedBytes,
                            TotalBytes = t.TotalBytes
                        });
                    }

                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = currentFile ?? "",
                        Done = done,
                        Total = tasks.Count,
                        ActiveTasks = snapshot,
                        TotalDownloadedBytes = totalDownloadedBytes
                    });
                }
            }

            var downloadTasks = tasks.Select(async task =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (File.Exists(task.Target))
                    {
                        var fi = new FileInfo(task.Target);
                        long minSize = task.MinSize > 0 ? task.MinSize : 1;
                        if (fi.Length >= minSize)
                        {
                            lock (lockObj) { done++; }
                            ReportProgress(task.Name);
                            return;
                        }
                        else
                        {
                            try { File.Delete(task.Target); } catch { }
                        }
                    }

                    var active = new DownloadTaskProgress
                    {
                        Key = task.Target,
                        Name = task.Name,
                        Url = task.Url
                    };
                    lock (lockObj) { activeTasks[task.Target] = active; }
                    ReportProgress(task.Name);

                    long prevRead = 0;
                    var lastReport = DateTime.MinValue;

                    await DownloadFileAsync(task.Url, task.Target, task.Name,
                        (read, total) =>
                        {
                            long delta = 0;
                            lock (lockObj)
                            {
                                delta = read - prevRead;
                                if (delta > 0)
                                {
                                    totalDownloadedBytes += delta;
                                    prevRead = read;
                                }
                                active.DownloadedBytes = read;
                                active.TotalBytes = total;
                            }

                            if (delta > 0)
                                ReportBytesTransferred(delta);

                            bool isSmallFile = total > 0 && total < 1024 * 1024;
                            bool isCompleted = total > 0 && read >= total;

                            var now = DateTime.UtcNow;
                            if (isSmallFile || isCompleted ||
                                (now - lastReport).TotalMilliseconds >= 100)
                            {
                                lastReport = now;
                                ReportProgress();
                            }
                        });

                    if (File.Exists(task.Target))
                    {
                        var fi = new FileInfo(task.Target);
                        long minSize = task.MinSize > 0 ? task.MinSize : 1;
                        if (fi.Length < minSize)
                        {
                            try { File.Delete(task.Target); } catch { }
                            throw new Exception(
                                $"下载不完整（{fi.Length} 字节 < 需要 {minSize}）");
                        }
                    }
                    else
                    {
                        throw new Exception("下载后文件不存在");
                    }

                    lock (lockObj)
                    {
                        activeTasks.Remove(task.Target);
                        done++;
                    }
                    ReportProgress(task.Name);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        activeTasks.Remove(task.Target);
                        failedTasks.Add(task);
                        done++;
                    }
                    ReportProgress($"[失败] {task.Name}");

                    try
                    {
                        if (File.Exists(task.Target))
                            File.Delete(task.Target);
                    }
                    catch { }

                    System.Diagnostics.Debug.WriteLine(
                        $"[{stageName} 第 {round} 轮失败] {task.Name}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(downloadTasks);

            return failedTasks;
        }

        private const int PerFileOverallTimeoutSec = 180;
        private const int SingleRequestTimeoutSec = 60;

        private static async Task DownloadFileAsync(
            string url, string target, string name,
            Action<long, long>? onProgress = null)
        {
            using var overallCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(PerFileOverallTimeoutSec));

            try
            {
                bool chunked = await TryChunkedDownloadAsync(
                    url, target, onProgress, overallCts.Token);

                if (chunked)
                    return;

                await DownloadFileCoreAsync(url, target, name, onProgress, overallCts.Token);
            }
            catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
            {
                throw new Exception($"文件下载超时（{PerFileOverallTimeoutSec} 秒）");
            }
        }

        private static async Task<bool> TryChunkedDownloadAsync(
            string url,
            string target,
            Action<long, long>? onProgress,
            CancellationToken overallCt)
        {
            long? size = null;
            try
            {
                size = await ProbeRemoteFileSizeAsync(url, overallCt);
            }
            catch { return false; }

            if (!size.HasValue || size.Value < ChunkedDownloadThreshold)
                return false;

            long fileSize = size.Value;

            int chunks = (int)Math.Min(
                fileSize / ChunkedMinChunkSize,
                ChunkedMaxChunks);

            if (chunks < 2) chunks = 2;

            long chunkSize = fileSize / chunks;

            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            try
            {
                using var handle = File.OpenHandle(
                    target,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Write,
                    FileOptions.None);

                RandomAccess.SetLength(handle, fileSize);

                long totalRead = 0;
                int successCount = 0;

                using var reportCts = new CancellationTokenSource();
                var reporterTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!reportCts.Token.IsCancellationRequested)
                        {
                            onProgress?.Invoke(
                                Interlocked.Read(ref totalRead), fileSize);
                            await Task.Delay(120, reportCts.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                });

                using var sem = new SemaphoreSlim(ChunkedMaxConcurrency);
                var tasks = new List<Task>();
                int failedChunks = 0;

                for (int i = 0; i < chunks; i++)
                {
                    int idx = i;
                    long start = idx * chunkSize;
                    long end = (idx == chunks - 1)
                        ? fileSize - 1
                        : (start + chunkSize - 1);

                    tasks.Add(Task.Run(async () =>
                    {
                        await sem.WaitAsync(overallCt);
                        try
                        {
                            bool ok = await DownloadChunkAsync(
                                url, handle, start, end,
                                delta => Interlocked.Add(ref totalRead, delta),
                                overallCt);

                            if (ok)
                                Interlocked.Increment(ref successCount);
                            else
                                Interlocked.Increment(ref failedChunks);
                        }
                        catch
                        {
                            Interlocked.Increment(ref failedChunks);
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }, overallCt));
                }

                try
                {
                    await Task.WhenAll(tasks);
                }
                finally
                {
                    reportCts.Cancel();
                    try { await reporterTask; } catch { }
                }

                onProgress?.Invoke(fileSize, fileSize);

                if (failedChunks > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Chunked] {Path.GetFileName(target)} 有 {failedChunks} 块失败，回退单连接");
                    try { File.Delete(target); } catch { }
                    return false;
                }

                var fi = new FileInfo(target);
                if (fi.Length != fileSize)
                {
                    try { File.Delete(target); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Chunked] {Path.GetFileName(target)} 分块失败：{ex.Message}");
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                return false;
            }
        }

        private static async Task<long?> ProbeRemoteFileSizeAsync(
            string url, CancellationToken overallCt)
        {
            var candidates = BuildUrlCandidates(url);

            foreach (var u in candidates)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallCt);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    using var req = new HttpRequestMessage(HttpMethod.Head, u);
                    req.Headers.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NodePulse/1.0");

                    using var resp = await _http.SendAsync(
                        req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    if (!resp.IsSuccessStatusCode)
                        continue;

                    var acceptRanges = resp.Headers.AcceptRanges;
                    bool supportsRange = acceptRanges.Any(r =>
                        string.Equals(r, "bytes", StringComparison.OrdinalIgnoreCase));

                    if (!supportsRange)
                        continue;

                    long? len = resp.Content.Headers.ContentLength;
                    if (len.HasValue && len.Value > 0)
                        return len;
                }
                catch { }
            }

            return null;
        }

        private static async Task<bool> DownloadChunkAsync(
            string url,
            SafeFileHandle handle,
            long start,
            long end,
            Action<long> reportDelta,
            CancellationToken overallCt)
        {
            long remainingStart = start;

            for (int attempt = 0; attempt < ChunkedChunkRetry; attempt++)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NodePulse/1.0");
                    req.Headers.Range = new RangeHeaderValue(remainingStart, end);

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallCt);
                    cts.CancelAfter(TimeSpan.FromSeconds(SingleRequestTimeoutSec));

                    using var resp = await _http.SendAsync(
                        req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                    if (resp.StatusCode != HttpStatusCode.PartialContent)
                        return false;

                    using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
                    var buffer = new byte[BufferSize];
                    long offset = remainingStart;
                    int n;

                    while ((n = await src.ReadAsync(buffer, cts.Token)) > 0)
                    {
                        await RandomAccess.WriteAsync(
                            handle, buffer.AsMemory(0, n), offset, cts.Token);
                        offset += n;
                        reportDelta(n);
                    }

                    if (offset >= end + 1)
                        return true;

                    remainingStart = offset;
                }
                catch (OperationCanceledException) when (overallCt.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Chunked] 块 [{start}-{end}] 第 {attempt + 1} 次失败: {ex.Message}");

                    try
                    {
                        await Task.Delay(500 * (attempt + 1), overallCt);
                    }
                    catch (OperationCanceledException) { throw; }
                }
            }

            return false;
        }

        private static async Task DownloadFileCoreAsync(
            string url, string target, string name,
            Action<long, long>? onProgress,
            CancellationToken overallCt)
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var candidates = BuildUrlCandidates(url);
            Exception? lastEx = null;
            int retryCount = RetryCount;

            foreach (var u in candidates)
            {
                for (int retry = 0; retry < retryCount; retry++)
                {
                    if (overallCt.IsCancellationRequested) throw new OperationCanceledException();

                    try
                    {
                        await ThrottleAsync();

                        long existingBytes = 0;
                        if (File.Exists(target))
                        {
                            try { existingBytes = new FileInfo(target).Length; }
                            catch { existingBytes = 0; }
                        }

                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(overallCt);
                        cts.CancelAfter(TimeSpan.FromSeconds(SingleRequestTimeoutSec));

                        using var req = new HttpRequestMessage(HttpMethod.Get, u);
                        req.Headers.UserAgent.ParseAdd(
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NodePulse/1.0");

                        if (existingBytes > 0)
                            req.Headers.Range = new RangeHeaderValue(existingBytes, null);

                        using var response = await _http.SendAsync(req,
                            HttpCompletionOption.ResponseHeadersRead, cts.Token);

                        if (response.StatusCode == HttpStatusCode.Forbidden ||
                            response.StatusCode == HttpStatusCode.NotFound)
                        {
                            throw new InvalidOperationException(
                                $"HTTP {(int)response.StatusCode}");
                        }
                        response.EnsureSuccessStatusCode();

                        bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                        long totalBytes = 0;

                        if (isPartial)
                        {
                            totalBytes = existingBytes +
                                (response.Content.Headers.ContentLength ?? 0);
                        }
                        else
                        {
                            existingBytes = 0;
                            totalBytes = response.Content.Headers.ContentLength ?? -1;
                        }

                        using var src = await response.Content.ReadAsStreamAsync(cts.Token);
                        FileStream dst;
                        if (isPartial && existingBytes > 0)
                            dst = new FileStream(target, FileMode.Append, FileAccess.Write);
                        else
                            dst = new FileStream(target, FileMode.Create, FileAccess.Write);

                        long readBytes;
                        using (dst)
                        {
                            readBytes = existingBytes;
                            var buffer = new byte[BufferSize];
                            int n;
                            while ((n = await src.ReadAsync(buffer, cts.Token)) > 0)
                            {
                                await dst.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                                readBytes += n;

                                onProgress?.Invoke(readBytes,
                                    totalBytes > 0 ? totalBytes : readBytes);
                            }
                            await dst.FlushAsync(cts.Token);
                        }

                        onProgress?.Invoke(readBytes,
                            totalBytes > 0 ? totalBytes : readBytes);

                        var finalFi = new FileInfo(target);
                        if (finalFi.Length == 0)
                            throw new Exception("下载得到 0 字节文件");

                        ReportRequestResult(true);
                        return;
                    }
                    catch (OperationCanceledException) when (overallCt.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        ReportRequestResult(false);

                        if (ex.Message.Contains("HTTP 403") ||
                            ex.Message.Contains("HTTP 404"))
                            break;

                        try
                        {
                            await Task.Delay(500 * (retry + 1), overallCt);
                        }
                        catch (OperationCanceledException) { throw; }
                    }
                }
            }

            throw new Exception($"下载失败：{url}\n{lastEx?.Message}");
        }

        private static List<string> BuildUrlCandidates(string originalUrl)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var mirrorUrl = ToMirrorUrl(originalUrl);
            bool canMirror = mirrorUrl != originalUrl;

            bool stalled;
            lock (_adaptiveLock) stalled = _stalled;

            if (stalled && canMirror)
            {
                if (seen.Add(mirrorUrl))
                    result.Add(mirrorUrl);
                if (seen.Add(originalUrl))
                    result.Add(originalUrl);
            }
            else
            {
                if (seen.Add(originalUrl))
                    result.Add(originalUrl);
                if (canMirror && seen.Add(mirrorUrl))
                    result.Add(mirrorUrl);
            }

            return result;
        }

        private static bool RulesAllow(JsonArray? rules)
        {
            if (rules is null || rules.Count == 0) return true;

            var osName = PlatformHelper.RulesOsName;
            var osArch = PlatformHelper.RulesOsArch;

            bool allowed = false;
            foreach (var ruleNode in rules)
            {
                if (ruleNode is not JsonObject rule) continue;

                var action = rule["action"]?.GetValue<string>() ?? "allow";
                bool match = true;

                if (rule["os"] is JsonObject os)
                {
                    var name = os["name"]?.GetValue<string>();
                    var arch = os["arch"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name) &&
                        !name.Equals(osName, StringComparison.OrdinalIgnoreCase))
                        match = false;
                    if (!string.IsNullOrEmpty(arch) &&
                        !arch.Equals(osArch, StringComparison.OrdinalIgnoreCase))
                        match = false;
                }

                if (rule["features"] is JsonObject features)
                {
                    foreach (var feat in features)
                    {
                        var want = feat.Value?.GetValue<bool>() ?? false;
                        bool has = feat.Key switch
                        {
                            "is_demo_user" => false,
                            "has_custom_resolution" => true,
                            "has_quick_plays_support" => false,
                            "is_quick_play_singleplayer" => false,
                            "is_quick_play_multiplayer" => false,
                            "is_quick_play_realms" => false,
                            _ => false
                        };
                        if (want != has)
                        {
                            match = false;
                            break;
                        }
                    }
                }

                if (match) allowed = action == "allow";
            }

            return allowed;
        }
    }

    // ============ 数据模型 ============

    public class VersionInfo
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Url { get; set; } = "";
        public string ReleaseTime { get; set; } = "";

        public bool IsRelease => Type == "release";
        public string TypeBadgeText => IsRelease ? "正式版" : "快照";
        public string TypeBadgeColor => IsRelease ? "#4CAF50" : "#FF9800";

        public string DisplayTime
        {
            get
            {
                if (DateTime.TryParse(ReleaseTime, out var dt))
                    return dt.ToLocalTime().ToString("yyyy年MM月dd日 HH:mm:ss");
                return ReleaseTime;
            }
        }
    }

    public class DownloadTask
    {
        public string Url { get; set; } = "";
        public string Target { get; set; } = "";
        public string Name { get; set; } = "";
        public long MinSize { get; set; } = 0;
    }

    public class DownloadProgress
    {
        public string CurrentFile { get; set; } = "";
        public int Done { get; set; }
        public int Total { get; set; }
        public double Percent => Total == 0 ? 0 : (double)Done / Total * 100;

        /// <summary>★ 大阶段名（可选）。设置后 UI 的"当前阶段"会用它更新</summary>
        public string? Stage { get; set; }

        public List<DownloadTaskProgress> ActiveTasks { get; set; } = new();
        public long TotalDownloadedBytes { get; set; }
    }

    public class DownloadTaskProgress
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Percent => TotalBytes > 0
            ? (double)DownloadedBytes / TotalBytes * 100
            : 0;
    }
}