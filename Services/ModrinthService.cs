using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class ModrinthService
    {
        private const string BaseUrl = "https://api.modrinth.com/v2";

        // ============ 缓存类别（仅保留项目/版本 JSON 缓存） ============
        private const string ProjectCacheCategory = "modrinth-projects";
        private const string VersionCacheCategory = "modrinth-versions";

        private static readonly TimeSpan ProjectCacheTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan VersionCacheTtl = TimeSpan.FromHours(1);

        /// <summary>图标缩略图尺寸</summary>
        private const int IconThumbnailSize = 64;

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static ModrinthService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "NodePulse/1.0 (https://github.com/nodepulse)");
        }

        // ================================================================
        // 搜索
        // ================================================================

        public static async Task<ModrinthSearchResponse?> SearchModsAsync(
            string query,
            string? gameVersion = null,
            string? loader = null,
            int offset = 0,
            int limit = 20,
            CancellationToken ct = default,
            bool sortByDownloads = false,
            string projectType = "mod")
        {
            try
            {
                var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? "")}" +
                          $"&limit={limit}&offset={offset}" +
                          $"&index={(sortByDownloads ? "downloads" : "relevance")}";

                var facetParts = new List<string> { $"[\"project_type:{projectType}\"]" };

                if (!string.IsNullOrEmpty(loader))
                    facetParts.Add($"[\"categories:{loader}\"]");

                if (!string.IsNullOrEmpty(gameVersion))
                    facetParts.Add($"[\"versions:{gameVersion}\"]");

                var facets = "[" + string.Join(",", facetParts) + "]";
                url += $"&facets={Uri.EscapeDataString(facets)}";

                var json = await _http.GetStringAsync(url, ct);
                return JsonSerializer.Deserialize<ModrinthSearchResponse>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Modrinth] 搜索失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 项目详情：带缓存
        // ================================================================

        public static async Task<ModrinthProject?> GetProjectAsync(
            string projectId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectId)) return null;

            try
            {
                var cached = CacheManager.Read(
                    ProjectCacheCategory, projectId, ".json", ProjectCacheTtl);

                if (cached != null && cached.Length > 0)
                {
                    var cachedJson = Encoding.UTF8.GetString(cached);
                    var cachedProject = JsonSerializer.Deserialize<ModrinthProject>(cachedJson);
                    if (cachedProject != null)
                        return cachedProject;
                }
            }
            catch { }

            try
            {
                var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}";
                var json = await _http.GetStringAsync(url, ct);

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    CacheManager.Write(ProjectCacheCategory, projectId, bytes, ".json");
                }
                catch { }

                return JsonSerializer.Deserialize<ModrinthProject>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Modrinth] 获取项目详情失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 版本列表：带缓存
        // ================================================================

        public static async Task<List<ModrinthVersion>> GetVersionsAsync(
            string projectId,
            string? gameVersion = null,
            string? loader = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(projectId)) return new();

            var cacheKey = $"{projectId}|{gameVersion ?? ""}|{loader ?? ""}";

            try
            {
                var cached = CacheManager.Read(
                    VersionCacheCategory, cacheKey, ".json", VersionCacheTtl);

                if (cached != null && cached.Length > 0)
                {
                    var cachedJson = Encoding.UTF8.GetString(cached);
                    var cachedVersions = JsonSerializer.Deserialize<List<ModrinthVersion>>(cachedJson);
                    if (cachedVersions != null)
                        return cachedVersions;
                }
            }
            catch { }

            try
            {
                var url = $"{BaseUrl}/project/{Uri.EscapeDataString(projectId)}/version";

                var queryParts = new List<string>();
                if (!string.IsNullOrEmpty(gameVersion))
                    queryParts.Add($"game_versions=[\"{Uri.EscapeDataString(gameVersion)}\"]");
                if (!string.IsNullOrEmpty(loader))
                    queryParts.Add($"loaders=[\"{Uri.EscapeDataString(loader)}\"]");

                if (queryParts.Count > 0)
                    url += "?" + string.Join("&", queryParts);

                var json = await _http.GetStringAsync(url, ct);

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    CacheManager.Write(VersionCacheCategory, cacheKey, bytes, ".json");
                }
                catch { }

                var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json);
                return versions ?? new();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Modrinth] 获取版本失败: {ex.Message}");
                return new();
            }
        }

        // ================================================================
        // ★ 图标：只走网络 + 请求 64×64 缩略图，不写缓存、不读缓存
        // ================================================================

        public static async Task<Bitmap?> LoadIconAsync(
            string? url,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var thumbUrl = BuildThumbnailUrl(url, IconThumbnailSize);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(15));

                var bytes = await _http.GetByteArrayAsync(thumbUrl, cts.Token);
                if (bytes.Length == 0) return null;

                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Modrinth] 图标加载失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 把 Modrinth 图床 URL 转成指定尺寸的缩略图 URL。
        /// </summary>
        private static string BuildThumbnailUrl(string original, int size)
        {
            if (string.IsNullOrEmpty(original)) return original;

            if (!original.Contains("cdn.modrinth.com"))
                return original;

            if (original.Contains("width="))
                return original;

            var sep = original.Contains('?') ? '&' : '?';
            return $"{original}{sep}width={size}&height={size}";
        }

        // ================================================================
        // 下载
        // ================================================================

        public static async Task<string> DownloadFileAsync(
            ModrinthFile file,
            string targetDir,
            Action<long, long>? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(targetDir);

            var targetPath = Path.Combine(targetDir, file.Filename);

            if (File.Exists(targetPath))
            {
                var fi = new FileInfo(targetPath);
                if (fi.Length == file.Size && file.Size > 0)
                    return targetPath;
            }

            await DownloadFileToPathAsync(file, targetPath, progress, ct);

            return targetPath;
        }

        public static async Task DownloadFileToPathAsync(
            ModrinthFile file,
            string targetPath,
            Action<long, long>? progress = null,
            CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var response = await _http.GetAsync(
                file.Url,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? file.Size;
            long readBytes = 0;

            using var src = await response.Content.ReadAsStreamAsync(ct);
            using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            int n;
            var lastReport = DateTime.MinValue;

            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                readBytes += n;

                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 150)
                {
                    lastReport = now;
                    progress?.Invoke(readBytes, totalBytes);
                }
            }

            await dst.FlushAsync(ct);
            progress?.Invoke(readBytes, totalBytes);
        }

        public static async Task DownloadUrlToPathAsync(
            string url,
            string targetPath,
            Action<long, long>? progress = null,
            CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var response = await _http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long readBytes = 0;

            using var src = await response.Content.ReadAsStreamAsync(ct);
            using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[81920];
            int n;
            var lastReport = DateTime.MinValue;

            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                readBytes += n;

                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds >= 150)
                {
                    lastReport = now;
                    progress?.Invoke(readBytes, totalBytes > 0 ? totalBytes : readBytes);
                }
            }

            await dst.FlushAsync(ct);
            progress?.Invoke(readBytes, totalBytes > 0 ? totalBytes : readBytes);
        }

        public static string? LoaderToModrinth(ModLoaderType type)
        {
            return type switch
            {
                ModLoaderType.Fabric => "fabric",
                ModLoaderType.Forge => "forge",
                ModLoaderType.NeoForge => "neoforge",
                ModLoaderType.Quilt => "quilt",
                _ => null
            };
        }
    }
}