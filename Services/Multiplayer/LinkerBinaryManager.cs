using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse.Services.Multiplayer
{
    /// <summary>
    /// Linker 二进制管理器。
    /// 
    /// ★ 不打包进启动器。用户可自行下载，或通过启动器内的一键下载功能获取。
    /// 
    /// 存放位置：启动器目录/.NodePulse/linker/
    /// 
    /// 期望的文件：
    ///     Windows  : linker.exe + wintun.dll
    ///     Linux    : linker
    ///     macOS    : linker
    /// </summary>
    public static class LinkerBinaryManager
    {
        private static readonly object _lock = new();
        private static string? _cachedPath;

        /// <summary>Linker 版本号（与下载 URL 中的版本保持一致）</summary>
        public const string LinkerVersion = "v2.0.20";

        /// <summary>下载服务器基地址</summary>
        private const string DownloadBaseUrl =
            "https://static.snltty.com/downloads/linker";

        /// <summary>每个平台需要的文件清单（第一个是可执行文件）</summary>
        private static readonly Dictionary<string, string[]> PlatformFiles = new()
        {
            ["windows-x64"]   = new[] { "linker.exe", "wintun.dll" },
            ["windows-arm64"] = new[] { "linker.exe", "wintun.dll" },
            ["linux-x64"]     = new[] { "linker" },
            ["linux-arm64"]   = new[] { "linker" },
            ["macos-x64"]     = new[] { "linker" },
            ["macos-arm64"]   = new[] { "linker" }
        };

        /// <summary>各平台对应的下载文件名（不含扩展名）</summary>
        private static readonly Dictionary<string, string> PlatformDownloadNames = new()
        {
            ["windows-x64"]   = "linker-win-x64",
            ["windows-arm64"] = "linker-win-arm64",
            ["linux-x64"]     = "linker-linux-x64",
            ["linux-arm64"]   = "linker-linux-arm64",
            ["macos-x64"]     = "linker-osx-x64",
            ["macos-arm64"]   = "linker-osx-arm64"
        };

        public const string DownloadPageUrl =
            "https://github.com/snltty/linker/releases";

        // ================================================================
        // 平台检测
        // ================================================================

        public static string? GetPlatformFolder()
        {
            if (OperatingSystem.IsAndroid()) return null;

            string os = OperatingSystem.IsWindows() ? "windows"
                     : OperatingSystem.IsLinux()   ? "linux"
                     : OperatingSystem.IsMacOS()   ? "macos"
                     : "";
            if (os == "") return null;

            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64   => "x64",
                Architecture.Arm64 => "arm64",
                _ => "x64"
            };

            return $"{os}-{arch}";
        }

        /// <summary>获取当前平台的下载 URL；不支持返回 null</summary>
        public static string? GetDownloadUrl()
        {
            var folder = GetPlatformFolder();
            if (string.IsNullOrEmpty(folder)) return null;

            if (!PlatformDownloadNames.TryGetValue(folder, out var downloadName))
                return null;

            return $"{DownloadBaseUrl}/{LinkerVersion}/{downloadName}.zip";
        }

        // ================================================================
        // 路径
        // ================================================================

        /// <summary>Linker 存放目录：启动器目录/.NodePulse/linker/</summary>
        public static string GetLinkerDir()
            => Path.Combine(AppContext.BaseDirectory, ".NodePulse", "linker");

        /// <summary>
        /// 检查 Linker 组件是否已就绪（所有必需文件都存在）。
        /// </summary>
        public static bool IsReady()
        {
            try
            {
                var folder = GetPlatformFolder();
                if (string.IsNullOrEmpty(folder)) return false;
                if (!PlatformFiles.TryGetValue(folder, out var files)) return false;

                var dir = GetLinkerDir();
                if (!Directory.Exists(dir)) return false;

                foreach (var name in files)
                {
                    var path = Path.Combine(dir, name);
                    if (!File.Exists(path) || new FileInfo(path).Length < 100)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // ================================================================
        // 下载 + 解压
        // ================================================================

        /// <summary>
        /// 下载当前平台对应的 Linker 压缩包，并解压到 GetLinkerDir()。
        /// ★ 保留 zip 内的全部文件（含子目录），不做任何删减。
        /// </summary>
        /// <param name="progress">进度回调（0-100）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>主可执行文件的完整路径</returns>
        public static async Task<string> DownloadAndExtractAsync(
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            var folder = GetPlatformFolder();
            if (string.IsNullOrEmpty(folder))
                throw new Exception("当前平台不支持 Linker");

            var downloadUrl = GetDownloadUrl();
            if (string.IsNullOrEmpty(downloadUrl))
                throw new Exception($"未找到平台 {folder} 的下载地址");

            if (!PlatformFiles.TryGetValue(folder, out var files) || files.Length == 0)
                throw new Exception($"未配置平台文件清单：{folder}");

            var targetDir = GetLinkerDir();
            Directory.CreateDirectory(targetDir);

            // 临时 zip 文件路径
            var tempZip = Path.Combine(Path.GetTempPath(),
                $"nodepulse-linker-{Guid.NewGuid():N}.zip");

            try
            {
                progress?.Report(0);

                // ---- 1. 下载 ----
                using (var http = new HttpClient
                {
                    Timeout = TimeSpan.FromMinutes(10)
                })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                    using var response = await http.GetAsync(
                        downloadUrl,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct);

                    response.EnsureSuccessStatusCode();

                    long totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long readBytes = 0;

                    await using var src = await response.Content.ReadAsStreamAsync(ct);
                    await using var dst = new FileStream(tempZip, FileMode.Create,
                        FileAccess.Write, FileShare.None);

                    var buffer = new byte[81920];
                    int n;
                    var lastReport = DateTime.MinValue;

                    while ((n = await src.ReadAsync(buffer, ct)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                        readBytes += n;

                        var now = DateTime.UtcNow;
                        if ((now - lastReport).TotalMilliseconds >= 100)
                        {
                            lastReport = now;
                            if (totalBytes > 0)
                                progress?.Report((double)readBytes / totalBytes * 80);
                        }
                    }

                    await dst.FlushAsync(ct);
                }

                if (!File.Exists(tempZip) || new FileInfo(tempZip).Length < 1000)
                    throw new Exception("下载的压缩包异常（文件过小）");

                progress?.Report(80);

                // ---- 2. 解压到临时目录 ----
                var tempExtractDir = Path.Combine(Path.GetTempPath(),
                    $"nodepulse-linker-extract-{Guid.NewGuid():N}");

                try
                {
                    ZipFile.ExtractToDirectory(tempZip, tempExtractDir, overwriteFiles: true);

                    progress?.Report(88);

                    // ---- 3. 定位主可执行文件 ----
                    // zip 里可能有顶层子目录（如 linker-win-x64/linker.exe），
                    // 我们要找到 files[0] 所在目录，然后整个目录内容整体拷贝，
                    // 保证 linker.exe 位于目标目录根部，同时不丢任何其他文件。
                    var mainExeName = files[0];
                    var mainExeFullPath = Directory
                        .GetFiles(tempExtractDir, mainExeName, SearchOption.AllDirectories)
                        .FirstOrDefault();

                    if (mainExeFullPath == null)
                    {
                        throw new Exception(
                            $"下载的压缩包里找不到主程序「{mainExeName}」。\n" +
                            "可能下载的压缩包损坏或版本不匹配，请重试。");
                    }

                    string sourceRoot = Path.GetDirectoryName(mainExeFullPath)!;

                    // ---- 4. 递归复制 sourceRoot 下全部内容到 targetDir ----
                    CopyDirectoryRecursive(sourceRoot, targetDir, overwrite: true);

                    progress?.Report(95);

                    // ---- 5. 校验必需文件 ----
                    var missing = new List<string>();
                    foreach (var name in files)
                    {
                        var path = Path.Combine(targetDir, name);
                        if (!File.Exists(path) || new FileInfo(path).Length < 100)
                            missing.Add(name);
                    }

                    if (missing.Count > 0)
                    {
                        throw new Exception(
                            $"解压后缺少以下文件：{string.Join(", ", missing)}\n" +
                            "下载的压缩包可能不完整或版本不匹配，请重试。");
                    }
                }
                finally
                {
                    try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
                }

                progress?.Report(100);

                // ---- 6. 权限 ----
                var exePath = Path.Combine(targetDir, files[0]);
                if (!OperatingSystem.IsWindows())
                    EnsureExecutablePermission(exePath);

                // ---- 7. 缓存 ----
                lock (_lock) { _cachedPath = exePath; }

                return exePath;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        /// <summary>
        /// 递归复制源目录下的所有文件和子目录到目标目录（保留结构，可覆盖）。
        /// </summary>
        private static void CopyDirectoryRecursive(
            string sourceDir, string targetDir, bool overwrite)
        {
            Directory.CreateDirectory(targetDir);

            // 复制所有文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite);
            }

            // 递归子目录
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir, overwrite);
            }
        }

        // ================================================================
        // 启动前检查
        // ================================================================

        /// <summary>
        /// 检查 Linker 是否已就绪。找不到抛异常。
        /// </summary>
        public static Task<string> EnsureLinkerAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (OperatingSystem.IsAndroid())
                throw new Exception("当前平台不支持 Linker");

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedPath) && File.Exists(_cachedPath))
                    return Task.FromResult(_cachedPath);
            }

            var folder = GetPlatformFolder();
            if (string.IsNullOrEmpty(folder))
                throw new Exception("当前平台不支持 Linker");

            if (!PlatformFiles.TryGetValue(folder, out var files) || files.Length == 0)
                throw new Exception($"未配置平台文件清单：{folder}");

            var dir = GetLinkerDir();

            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
                throw new Exception(
                    "Linker 组件未下载。\n\n" +
                    "请点击「一键下载 Linker」按钮自动获取。");
            }

            var missing = new List<string>();
            foreach (var name in files)
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path) || new FileInfo(path).Length < 100)
                    missing.Add(name);
            }

            if (missing.Count > 0)
            {
                throw new Exception(
                    "Linker 组件不完整，缺少以下文件：\n" +
                    $"    {string.Join("\n    ", missing)}\n\n" +
                    "请点击「一键下载 Linker」按钮自动获取。");
            }

            var exePath = Path.Combine(dir, files[0]);

            if (!OperatingSystem.IsWindows())
                EnsureExecutablePermission(exePath);

            lock (_lock) { _cachedPath = exePath; }
            return Task.FromResult(exePath);
        }

        private static void EnsureExecutablePermission(string path)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path,
                    mode | UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[LinkerBinaryManager] chmod 失败（可忽略）：{ex.Message}");
            }
        }

        public static void ClearCache()
        {
            lock (_lock) { _cachedPath = null; }
        }
    }
}