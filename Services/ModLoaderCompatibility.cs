using System;
using System.Collections.Generic;
using NodePulse.Models;

namespace NodePulse.Services
{
    /// <summary>
    /// 模组加载器兼容性数据库（本地 fallback）。
    /// 主逻辑走联网，本表仅在联网失败或返回空时使用。
    /// 
    /// ★ 版本号格式说明：
    ///   - 旧格式：1.20.1 / 1.21.1 / 1.7.10（2025 年及以前）
    ///   - 新格式：26.1 / 26.2 / 26.1.2（2026 年起，Mojang 改用年份命名）
    /// </summary>
    public static class ModLoaderCompatibility
    {
        // ================================================================
        // 是否支持某加载器
        // ================================================================

        public static bool Supports(string mcVersion, ModLoaderType loader)
        {
            return loader switch
            {
                ModLoaderType.None => true,
                ModLoaderType.Fabric => SupportsFabric(mcVersion),
                ModLoaderType.Forge => SupportsForge(mcVersion),
                ModLoaderType.NeoForge => SupportsNeoForge(mcVersion),
                ModLoaderType.Quilt => SupportsQuilt(mcVersion),
                ModLoaderType.OptiFine => SupportsOptiFine(mcVersion),
                _ => false
            };
        }

        private static bool SupportsFabric(string mcVer) => McAtLeast(mcVer, 14);
        private static bool SupportsQuilt(string mcVer) => McAtLeast(mcVer, 14);

        /// <summary>
        /// Forge 从 1.1 起支持，至今仍在更新。
        /// 新格式 26.x 起继续支持。
        /// </summary>
        private static bool SupportsForge(string mcVer)
        {
            if (string.IsNullOrEmpty(mcVer)) return false;

            // 旧格式：1.x 全部支持
            if (mcVer.StartsWith("1.")) return true;

            // 新格式：26.1 / 27.1 等（年份.发布）
            var firstDot = mcVer.IndexOf('.');
            if (firstDot > 0 &&
                int.TryParse(mcVer.Substring(0, firstDot), out var year))
            {
                return year >= 20;
            }

            return false;
        }

        // NeoForge 从 1.20.1 起支持
        private static bool SupportsNeoForge(string mcVer) => McAtLeast(mcVer, 20, 1);

        // OptiFine 只支持正式版
        private static bool SupportsOptiFine(string mcVer)
        {
            var (m, _) = ParseMc(mcVer);
            return m >= 1;
        }

        // ================================================================
        // 获取可用版本号列表（本地 fallback）
        // ================================================================

        public static List<string> GetVersions(string mcVersion, ModLoaderType loader)
        {
            return loader switch
            {
                ModLoaderType.Fabric => GetFabricVersions(mcVersion),
                ModLoaderType.Forge => GetForgeVersions(mcVersion),
                ModLoaderType.NeoForge => GetNeoForgeVersions(mcVersion),
                ModLoaderType.Quilt => GetQuiltVersions(mcVersion),
                ModLoaderType.OptiFine => GetOptiFineVersions(mcVersion),
                _ => new List<string>()
            };
        }

        // ---------- Fabric Loader ----------
        private static readonly string[] FabricLoaderVersions =
        {
            "0.16.10", "0.16.9", "0.16.8", "0.16.7",
            "0.16.5", "0.16.4", "0.16.3", "0.16.2", "0.16.0",
            "0.15.11", "0.15.10", "0.15.9", "0.15.7",
            "0.15.6", "0.15.4", "0.15.3",
            "0.15.0", "0.14.25", "0.14.24", "0.14.22"
        };

        private static List<string> GetFabricVersions(string mcVer)
        {
            if (!SupportsFabric(mcVer)) return new();
            return new List<string>(FabricLoaderVersions);
        }

        // ---------- Quilt Loader ----------
        private static readonly string[] QuiltLoaderVersions =
        {
            "0.27.1", "0.27.0", "0.26.4", "0.26.3",
            "0.26.0", "0.25.0", "0.24.0", "0.23.1",
            "0.23.0", "0.22.0", "0.21.2", "0.21.0",
            "0.20.0", "0.19.3", "0.19.1"
        };

        private static List<string> GetQuiltVersions(string mcVer)
        {
            if (!SupportsQuilt(mcVer)) return new();
            return new List<string>(QuiltLoaderVersions);
        }

        // ---------- Forge（本地 fallback） ----------
        private static readonly Dictionary<string, string> ForgeTable = new()
        {
            { "1.21.5", "55.1.13" },
            { "1.21.4", "54.1.18" },
            { "1.21.3", "53.1.12" },
            { "1.21.1", "52.1.16" },
            { "1.21",   "51.0.33" },
            { "1.20.6", "50.2.10" },
            { "1.20.4", "49.0.30" },
            { "1.20.1", "47.3.0" },
            { "1.19.4", "45.1.0" },
            { "1.19.2", "43.3.5" },
            { "1.18.2", "40.2.10" },
            { "1.17.1", "37.1.1" },
            { "1.16.5", "36.2.42" },
            { "1.15.2", "31.2.57" },
            { "1.14.4", "28.2.26" },
            { "1.12.2", "14.23.5.2859" },
            { "1.10.2", "12.18.3.2511" },
            { "1.7.10", "10.13.4.1614" }
        };

        private static List<string> GetForgeVersions(string mcVer)
        {
            if (!SupportsForge(mcVer)) return new();

            var result = new List<string>();
            if (ForgeTable.TryGetValue(mcVer, out var v))
                result.Add(v);

            return result;
        }

        // ---------- NeoForge（本地 fallback） ----------
        private static readonly Dictionary<string, string> NeoForgeTable = new()
        {
            { "1.20.1", "47.1.79" },
            { "1.20.2", "20.2.88" },
            { "1.20.3", "20.3.12" },
            { "1.20.4", "20.4.251" },
            { "1.20.6", "20.6.139" },
            { "1.21",   "21.0.167" },
            { "1.21.1", "21.1.217" },
            { "1.21.3", "21.3.97" },
            { "1.21.4", "21.4.156" },
            { "1.21.5", "21.5.89" }
        };

        private static List<string> GetNeoForgeVersions(string mcVer)
        {
            if (!SupportsNeoForge(mcVer)) return new();

            var result = new List<string>();
            if (NeoForgeTable.TryGetValue(mcVer, out var v))
                result.Add(v);

            return result;
        }

        // ---------- OptiFine（本地 fallback） ----------
        private static readonly Dictionary<string, string> OptiFineTable = new()
        {
            { "1.21.4", "HD_U_J3" },
            { "1.21.1", "HD_U_J1" },
            { "1.21",   "HD_U_J1_pre5" },
            { "1.20.6", "HD_U_I7" },
            { "1.20.4", "HD_U_I6" },
            { "1.20.2", "HD_U_I6" },
            { "1.20.1", "HD_U_I6" },
            { "1.20",   "HD_U_I2" },
            { "1.19.4", "HD_U_I5" },
            { "1.19.3", "HD_U_I4" },
            { "1.19.2", "HD_U_I2" },
            { "1.19.1", "HD_U_H9" },
            { "1.19",   "HD_U_H9" },
            { "1.18.2", "HD_U_H7" },
            { "1.18.1", "HD_U_H4" },
            { "1.18",   "HD_U_H4" },
            { "1.17.1", "HD_U_H1" },
            { "1.16.5", "HD_U_G8" },
            { "1.16.4", "HD_U_G7" },
            { "1.16.3", "HD_U_G5" },
            { "1.15.2", "HD_U_G5" },
            { "1.14.4", "HD_U_F5" },
            { "1.13.2", "HD_U_E4" },
            { "1.12.2", "HD_U_F5" },
            { "1.12.1", "HD_U_C4" },
            { "1.12",   "HD_U_C4" },
            { "1.11.2", "HD_U_C6" },
            { "1.10.2", "HD_U_E7" },
            { "1.9.4",  "HD_U_D5" },
            { "1.8.9",  "HD_U_M5" },
            { "1.8",    "HD_U_H7" },
            { "1.7.10", "HD_U_E7" }
        };

        private static List<string> GetOptiFineVersions(string mcVer)
        {
            if (!SupportsOptiFine(mcVer)) return new();

            var result = new List<string>();
            if (OptiFineTable.TryGetValue(mcVer, out var v))
                result.Add(v);

            return result;
        }

        // ================================================================
        // ★ 版本号解析工具：兼容新旧两种格式
        // ================================================================

        /// <summary>
        /// "1.20.1" → (20, 1)
        /// "1.21"   → (21, 0)
        /// "26.1"   → (26, 1)      ← 新格式（2026 年起）
        /// "26.1.2" → (26, 1)      ← 忽略 patch 段
        /// 无法解析 → (0, 0)
        /// </summary>
        private static (int minor, int patch) ParseMc(string v)
        {
            if (string.IsNullOrEmpty(v))
                return (0, 0);

            // 旧格式：1.x.x
            if (v.StartsWith("1."))
            {
                var s = v.Substring(2);
                var parts = s.Split('.');
                int minor = 0, patch = 0;
                if (parts.Length >= 1 && int.TryParse(parts[0], out var m)) minor = m;
                if (parts.Length >= 2 && int.TryParse(parts[1], out var p)) patch = p;
                return (minor, patch);
            }

            // 新格式：年份.发布[.补丁]
            var parts2 = v.Split('.');
            int minor2 = 0, patch2 = 0;
            if (parts2.Length >= 1 && int.TryParse(parts2[0], out var a)) minor2 = a;
            if (parts2.Length >= 2 && int.TryParse(parts2[1], out var b)) patch2 = b;
            return (minor2, patch2);
        }

        private static bool McAtLeast(string mcVer, int minor, int patch = 0)
        {
            var (m, p) = ParseMc(mcVer);
            if (m != minor) return m > minor;
            return p >= patch;
        }
    }
}