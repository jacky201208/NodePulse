using NodePulse.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace NodePulse.Services
{
    public static class CurseForgeService
    {
        private const string BaseUrl = "https://bmclapi2.bangbang93.com";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static CurseForgeService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "NodePulse/1.0 (https://github.com/nodepulse)");
        }

        // ================================================================
        // 搜索
        // ================================================================

        public static async Task<CfSearchResponse?> SearchModsAsync(
            string query,
            string? gameVersion = null,
            string? loader = null,
            int offset = 0,
            int limit = 20,
            bool sortByDownloads = false,
            int? classId = null,
            CancellationToken ct = default)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"gameId=432",
                    $"pageSize={limit}",
                    $"index={offset}"
                };

                if (!string.IsNullOrWhiteSpace(query))
                    queryParams.Add($"searchFilter={Uri.EscapeDataString(query)}");

                if (!string.IsNullOrWhiteSpace(gameVersion))
                    queryParams.Add($"gameVersion={Uri.EscapeDataString(gameVersion)}");

                if (classId.HasValue)
                    queryParams.Add($"classId={classId.Value}");

                queryParams.Add($"sortField={(sortByDownloads ? 6 : 2)}");
                queryParams.Add("sortOrder=desc");

                var url = $"{BaseUrl}/mc-mods?{string.Join("&", queryParams)}";

                var json = await _http.GetStringAsync(url, ct);
                return JsonSerializer.Deserialize<CfSearchResponse>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CurseForge] 搜索失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 模组详情
        // ================================================================

        public static async Task<CfModDetail?> GetModDetailAsync(
            long addonId,
            CancellationToken ct = default)
        {
            try
            {
                var url = $"{BaseUrl}/mc-addon/{addonId}";
                var json = await _http.GetStringAsync(url, ct);
                var resp = JsonSerializer.Deserialize<CfModDetailResponse>(json);
                return resp?.Data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CurseForge] 获取详情失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 文件列表
        // ================================================================

        public static async Task<List<CfFile>> GetFilesAsync(
            long addonId,
            CancellationToken ct = default)
        {
            try
            {
                var url = $"{BaseUrl}/mc-addon/{addonId}/files";
                var json = await _http.GetStringAsync(url, ct);
                var resp = JsonSerializer.Deserialize<CfFilesResponse>(json);
                return resp?.Data ?? new();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CurseForge] 获取文件列表失败: {ex.Message}");
                return new();
            }
        }

        // ================================================================
        // 下载链接（BMCLAPI 返回实际 URL）
        // ================================================================

        public static async Task<string?> GetDownloadUrlAsync(
            long addonId,
            long fileId,
            CancellationToken ct = default)
        {
            try
            {
                var url = $"{BaseUrl}/mc-addon/{addonId}/files/{fileId}/download-url";
                var json = await _http.GetStringAsync(url, ct);
                var resp = JsonSerializer.Deserialize<CfDownloadUrlResponse>(json);
                return resp?.Data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CurseForge] 获取下载链接失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 图标加载
        // ================================================================

        public static async Task<Bitmap?> LoadIconAsync(
            string? url,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url)) return null;

            try
            {
                var bytes = await _http.GetByteArrayAsync(url, ct);
                if (bytes.Length == 0) return null;

                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CurseForge] 图标加载失败: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 文件下载
        // ================================================================

        public static async Task DownloadFileToPathAsync(
            long addonId,
            long fileId,
            string targetPath,
            Action<long, long>? progress = null,
            CancellationToken ct = default)
        {
            var downloadUrl = await GetDownloadUrlAsync(addonId, fileId, ct);
            if (string.IsNullOrEmpty(downloadUrl))
                throw new Exception("无法获取下载链接");

            await ModrinthService.DownloadUrlToPathAsync(downloadUrl, targetPath, progress, ct);
        }

        // ================================================================
        // 分类 ID 映射
        // ================================================================

        public static int GetClassId(string projectType) => projectType switch
        {
            "mod" => 6,
            "modpack" => 4471,
            "datapack" => 4546,
            "shader" => 4548,
            _ => 6
        };
    }
}