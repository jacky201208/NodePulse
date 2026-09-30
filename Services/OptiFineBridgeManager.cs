using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace NodePulse.Services
{
    /// <summary>
    /// OptiFine 辅助资源管理器。
    /// 
    /// 负责从 Assets/OptiFine/ 释放以下文件到用户目录：
    ///   - launchwrapper-of-2.3.jar  （sp614x patch 过的 launchwrapper，绕开 Java 9+ ClassLoader 兼容问题）
    /// 
    /// 释放位置：启动器目录/.NodePulse/optifine/
    /// </summary>
    public static class OptiFineBridgeManager
    {
        private static readonly object _lock = new();
        private static string? _cachedLaunchwrapperPath;

        private const string AssetRoot = "avares://NodePulse/Assets/OptiFine";

        /// <summary>launchwrapper-of 的版本（与文件名保持一致）</summary>
        public const string LaunchwrapperVersion = "2.3";

        /// <summary>launchwrapper-of 的文件名</summary>
        public const string LaunchwrapperFileName = "launchwrapper-of-2.3.jar";

        /// <summary>launchwrapper-of 的最小有效大小</summary>
        private const long LaunchwrapperMinSize = 5_000;

        // ================================================================
        // 释放 launchwrapper-of
        // ================================================================

        /// <summary>
        /// 确保 launchwrapper-of-2.3.jar 已就绪，返回 jar 的绝对路径。
        /// </summary>
        public static async Task<string> EnsureLaunchwrapperAsync(
            CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedLaunchwrapperPath)
                    && File.Exists(_cachedLaunchwrapperPath))
                    return _cachedLaunchwrapperPath;
            }

            string targetDir = GetResourceDir();
            Directory.CreateDirectory(targetDir);

            string targetPath = Path.Combine(targetDir, LaunchwrapperFileName);

            // 已存在且大小正常 → 直接用
            if (File.Exists(targetPath)
                && new FileInfo(targetPath).Length > LaunchwrapperMinSize)
            {
                lock (_lock) { _cachedLaunchwrapperPath = targetPath; }
                return targetPath;
            }

            // 从 Assets 释放
            var uri = new Uri($"{AssetRoot}/{LaunchwrapperFileName}");
            if (!AssetLoader.Exists(uri))
            {
                throw new Exception(
                    $"启动器缺少资源文件：{uri}\n\n" +
                    $"请确认项目里存在 Assets/OptiFine/{LaunchwrapperFileName}，" +
                    $"并且 NodePulse.csproj 里已加入对应的 AvaloniaResource。");
            }

            try
            {
                await using (var src = AssetLoader.Open(uri))
                await using (var dst = new FileStream(
                    targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst, ct);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"释放 launchwrapper-of 失败：{ex.Message}", ex);
            }

            if (!File.Exists(targetPath)
                || new FileInfo(targetPath).Length < LaunchwrapperMinSize)
                throw new Exception("释放出的 launchwrapper-of 异常（文件过小）");

            lock (_lock) { _cachedLaunchwrapperPath = targetPath; }

            Debug.WriteLine($"[OptiFineBridgeManager] launchwrapper-of 已释放到：{targetPath}");
            return targetPath;
        }

        // ================================================================
        // 路径与清理
        // ================================================================

        /// <summary>资源工作目录：启动器目录/.NodePulse/optifine/</summary>
        public static string GetResourceDir()
            => Path.Combine(AppContext.BaseDirectory, ".NodePulse", "optifine");

        /// <summary>清空缓存的资源（下次调用会重新从 Assets 释放）</summary>
        public static void ClearCache()
        {
            try
            {
                var dir = GetResourceDir();
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);

                lock (_lock)
                {
                    _cachedLaunchwrapperPath = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OptiFineBridgeManager] 清理失败：{ex.Message}");
            }
        }

        // ================================================================
        // 兼容旧 API（如果有旧代码引用）
        // ================================================================

        [Obsolete("桥接工具已弃用，请使用 EnsureLaunchwrapperAsync")]
        public static Task<string> EnsureBridgeAsync(CancellationToken ct = default)
            => EnsureLaunchwrapperAsync(ct);

        [Obsolete("桥接工具已弃用，请使用 GetResourceDir")]
        public static string GetBridgeDir() => GetResourceDir();
    }
}