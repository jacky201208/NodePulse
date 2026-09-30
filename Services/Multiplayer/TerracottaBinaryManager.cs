using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace NodePulse.Services.Multiplayer
{
    /// <summary>
    /// 陶瓦联机二进制管理器。
    /// 负责从 Assets/Terracotta/{当前平台}/ 释放所需文件到用户目录。
    /// 安卓端不走释放流程（.so 由 APK 自动部署）。
    /// </summary>
    public static class TerracottaBinaryManager
    {
        private static readonly object _lock = new();
        private static string? _cachedExePath;

        private const string AssetRoot = "avares://NodePulse/Assets/Terracotta";

        /// <summary>
        /// 每个平台需要释放的文件列表。
        /// 第一个是主可执行文件，其余是依赖。
        /// ★ Windows 必须包含 VCRUNTIME140.DLL，否则用户机器上可能闪退。
        /// </summary>
        private static readonly Dictionary<string, string[]> PlatformFiles = new()
        {
            ["windows-x64"] = new[] { "terracotta.exe", "VCRUNTIME140.DLL" },
            ["windows-arm64"] = new[] { "terracotta.exe", "VCRUNTIME140.DLL" },
            ["linux-x64"] = new[] { "terracotta" },
            ["linux-arm64"] = new[] { "terracotta" },
            ["macos-x64"] = new[] { "terracotta" },
            ["macos-arm64"] = new[] { "terracotta" }
        };

        // ================================================================
        // 平台 → 资源子目录
        // ================================================================

        public static string? GetAssetFolder()
        {
            if (OperatingSystem.IsAndroid()) return null;

            string os;
            if (OperatingSystem.IsWindows()) os = "windows";
            else if (OperatingSystem.IsLinux()) os = "linux";
            else if (OperatingSystem.IsMacOS()) os = "macos";
            else return null;

            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                _ => "x64"
            };

            return $"{os}-{arch}";
        }

        public static string GetBinaryFileName()
            => OperatingSystem.IsWindows() ? "terracotta.exe" : "terracotta";

        // ================================================================
        // 释放可执行文件（含所有依赖）
        // ================================================================

        /// <summary>
        /// 确保陶瓦联机所有文件已就绪。
        /// 返回主可执行文件的绝对路径；安卓端返回 null。
        /// </summary>
        public static async Task<string?> EnsureExecutableAsync(
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (OperatingSystem.IsAndroid())
                return null;

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedExePath) && File.Exists(_cachedExePath))
                    return _cachedExePath;
            }

            string? folder = GetAssetFolder();
            if (string.IsNullOrEmpty(folder))
                throw new Exception("当前平台不支持陶瓦联机");

            if (!PlatformFiles.TryGetValue(folder, out var files) || files.Length == 0)
                throw new Exception($"未配置平台文件清单：{folder}");

            string targetDir = GetTerracottaDir();
            Directory.CreateDirectory(targetDir);

            string exePath = Path.Combine(targetDir, files[0]);

            // 检查所有文件是否已就绪
            bool allReady = true;
            foreach (var name in files)
            {
                var path = Path.Combine(targetDir, name);
                if (!File.Exists(path) || new FileInfo(path).Length < 100)
                {
                    allReady = false;
                    break;
                }
            }

            if (!allReady)
            {
                progress?.Report($"正在释放陶瓦联机文件到 {targetDir}...");

                foreach (var name in files)
                {
                    var assetUri = new Uri($"{AssetRoot}/{folder}/{name}");
                    var targetPath = Path.Combine(targetDir, name);

                    if (!AssetLoader.Exists(assetUri))
                    {
                        throw new Exception(
                            $"缺少资源文件：{assetUri}\n" +
                            $"请确认 Assets/Terracotta/{folder}/{name} 存在。");
                    }

                    try
                    {
                        await using (var src = AssetLoader.Open(assetUri))
                        await using (var dst = new FileStream(targetPath, FileMode.Create,
                            FileAccess.Write, FileShare.None))
                        {
                            await src.CopyToAsync(dst, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"释放 {name} 失败：{ex.Message}", ex);
                    }

                    if (name.Equals(files[0], StringComparison.OrdinalIgnoreCase))
                        EnsureExecutablePermission(targetPath);
                }
            }
            else
            {
                // 已存在，只确保可执行权限
                EnsureExecutablePermission(exePath);
            }

            if (!File.Exists(exePath) || new FileInfo(exePath).Length < 100)
                throw new Exception("释放出的陶瓦联机主程序异常");

            lock (_lock) { _cachedExePath = exePath; }
            return exePath;
        }

        // ================================================================
        // 路径与权限
        // ================================================================

        /// <summary>
        /// 陶瓦联机工作目录（用户级别）。
        /// </summary>
        public static string GetTerracottaDir()
        {
            string baseDir = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppContext.BaseDirectory;

            return Path.Combine(baseDir, "NodePulse", "terracotta");
        }

        private static void EnsureExecutablePermission(string path)
        {
            if (OperatingSystem.IsWindows()) return;

            try
            {
                var mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path,
                    mode |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TerracottaBinaryManager] chmod 失败（可忽略）：{ex.Message}");
            }
        }

        // ================================================================
        // 清理
        // ================================================================

        public static void ClearCache()
        {
            try
            {
                var dir = GetTerracottaDir();
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                lock (_lock) { _cachedExePath = null; }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[TerracottaBinaryManager] 清理失败：{ex.Message}");
            }
        }
    }
}