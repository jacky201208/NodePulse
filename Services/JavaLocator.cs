using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class JavaLocator
    {
        public static string FindJavaWithOverride(int requireMajor, string? manualPath)
        {
            // ============================================================
            // 1. 用户手动指定优先 —— 无条件使用（用户有最终决定权）
            // ============================================================
            if (!string.IsNullOrWhiteSpace(manualPath) && File.Exists(manualPath))
            {
                string path = manualPath;

                // Windows 下把 java.exe 换成 javaw.exe（无控制台）
                if (PlatformHelper.IsWindows &&
                    path.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
                {
                    var alt = Path.Combine(Path.GetDirectoryName(path)!, "javaw.exe");
                    if (File.Exists(alt)) path = alt;
                }

                // ★ 直接返回，不再检查版本，用户选了就用它
                return path;
            }

            var allJava = JavaInfo.ScanAll();
            if (allJava.Count == 0)
                throw new Exception("系统中未找到任何 Java 运行时");

            // 2. 精确匹配所需主版本
            var exact = allJava.FirstOrDefault(j => j.MajorVersion == requireMajor);
            if (exact != null)
                return exact.JavaPath;

            // 3. 找最小的 > requireMajor（而非最高）
            var bigger = allJava
                .Where(j => j.MajorVersion > requireMajor)
                .OrderBy(j => j.MajorVersion)
                .FirstOrDefault();
            if (bigger != null)
                return bigger.JavaPath;

            // 4. 兜底：都用不了，返回最高的（游戏会报错，但至少给个路径）
            return allJava.OrderByDescending(j => j.MajorVersion).First().JavaPath;
        }
    }
}