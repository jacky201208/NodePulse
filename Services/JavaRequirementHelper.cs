using System;

namespace NodePulse.Services
{
    /// <summary>
    /// 版本号解析 + Java 需求推断。
    /// 
    /// ★ 兼容两种版本格式：
    ///   - 旧格式：1.20.1 / 1.21.1 / 1.7.10（2025 年及以前）
    ///   - 新格式：26.1 / 26.2 / 27.1（2026 年起，Mojang 改用年份命名）
    /// </summary>
    public static class JavaRequirementHelper
    {
        /// <summary>
        /// 解析 MC 版本号。
        /// "1.20.1" → (20, 1)
        /// "1.21"   → (21, 0)
        /// "26.1"   → (26, 1)      ← 新格式
        /// "26.1.2" → (26, 1)      ← 忽略 patch
        /// 无法解析 → (0, 0)
        /// </summary>
        public static (int minor, int patch) ParseMc(string v)
        {
            if (string.IsNullOrEmpty(v))
                return (0, 0);

            // 旧格式：1.x
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

        /// <summary>
        /// 推断某 MC 版本运行安装器/游戏时需要的最低 Java 主版本。
        /// 
        /// 旧格式规则：
        ///   1.x    (x &lt; 17) → Java 8
        ///   1.17            → Java 16
        ///   1.18 ~ 1.20     → Java 17
        ///   1.21+           → Java 21
        /// 
        /// 新格式规则（2026 起）：
        ///   26.1 / 27.1 ... → Java 21
        /// </summary>
        public static int GetRequiredJavaFor(string mcVersion)
        {
            if (string.IsNullOrEmpty(mcVersion))
                return 17;

            // ---- 新格式：年份命名 ----
            if (!mcVersion.StartsWith("1."))
            {
                // 2026 起的 MC（26.1 等）用 Java 21+
                return 21;
            }

            // ---- 旧格式 ----
            var (minor, _) = ParseMc(mcVersion);
            if (minor < 17) return 8;
            if (minor == 17) return 16;
            if (minor >= 18 && minor <= 20) return 17;
            if (minor >= 21) return 21;
            return 21;
        }
    }
}