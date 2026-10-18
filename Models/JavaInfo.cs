using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NodePulse.Models
{
    public class JavaInfo
    {
        [JsonPropertyName("javaPath")]
        public string JavaPath { get; set; } = "";

        [JsonPropertyName("majorVersion")]
        public int MajorVersion { get; set; } = 0;

        [JsonPropertyName("fullVersion")]
        public string FullVersion { get; set; } = "";

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = "";

        [JsonIgnore]
        public string DisplayText =>
            $"Java {MajorVersion,-2}  ({FullVersion})  -  {JavaPath}";

        // ================================================================
        // ★ 缓存：存到 .NodePulse/cache/java-list.json
        // ================================================================

        private static string GetCachePath()
            => Path.Combine(AppContext.BaseDirectory, ".NodePulse", "cache", "java-list.json");

        private static readonly JsonSerializerOptions CacheJsonOpts = new()
        {
            WriteIndented = true
        };

        private static List<JavaInfo>? LoadCache()
        {
            try
            {
                var path = GetCachePath();
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<JavaInfo>>(json, CacheJsonOpts);
            }
            catch { return null; }
        }

        private static void SaveCache(List<JavaInfo> list)
        {
            try
            {
                var path = GetCachePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(list, CacheJsonOpts);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        /// <summary>清除 Java 缓存（下次进入 Java Tab 会重新扫描）</summary>
        public static void ClearCache()
        {
            try
            {
                var path = GetCachePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        /// <summary>把单个 Java 追加到缓存（导入时用）</summary>
        public static void AppendToCache(JavaInfo info)
        {
            if (info == null) return;
            try
            {
                var list = LoadCache() ?? new List<JavaInfo>();
                if (list.Any(j => string.Equals(j.JavaPath, info.JavaPath,
                        StringComparison.OrdinalIgnoreCase)))
                    return;

                list.Insert(0, info);
                SaveCache(list);
            }
            catch { }
        }

        // ================================================================
        // ★ 对外入口：默认走缓存，forceRefresh=true 才重新扫描
        // ================================================================

        public static List<JavaInfo> ScanAll(bool forceRefresh = false)
        {
            // 1. 非强制 → 尝试读缓存
            if (!forceRefresh)
            {
                var cached = LoadCache();
                if (cached != null && cached.Count > 0)
                {
                    // 校验缓存里的 java 是否还在磁盘上（用户可能卸载了）
                    var valid = cached
                        .Where(j => !string.IsNullOrEmpty(j.JavaPath)
                                    && File.Exists(j.JavaPath))
                        .ToList();

                    if (valid.Count > 0)
                        return valid;

                    // 全部失效 → 继续走扫描
                }
            }

            // 2. 扫描 + 写缓存
            var result = ScanAllCore();
            SaveCache(result);
            return result;
        }

        // ================================================================
        // 实际扫描逻辑（原逻辑保持不变）
        // ================================================================

        private static List<JavaInfo> ScanAllCore()
        {
            var result = new List<JavaInfo>();

            var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var probeName = Services.PlatformHelper.IsWindows ? "java.exe" : "java";
            var exeName = Services.PlatformHelper.GetJavaExecutableName();

            void TryAdd(string exePath)
            {
                try
                {
                    if (string.IsNullOrEmpty(exePath)) return;
                    if (!File.Exists(exePath)) return;

                    var dir = Path.GetDirectoryName(exePath);
                    if (string.IsNullOrEmpty(dir)) return;
                    if (!seenDirs.Add(dir)) return;

                    var info = Probe(exePath);
                    if (info is not null) result.Add(info);
                }
                catch { }
            }

            void TryAddDir(string binDir)
            {
                try
                {
                    if (string.IsNullOrEmpty(binDir)) return;
                    if (!Directory.Exists(binDir)) return;

                    var javaExe = Path.Combine(binDir, probeName);
                    if (File.Exists(javaExe)) { TryAdd(javaExe); return; }

                    var exe = Path.Combine(binDir, exeName);
                    if (File.Exists(exe)) TryAdd(exe);
                }
                catch { }
            }

            void ScanRuntimeRoot(string runtimeRoot)
            {
                try
                {
                    if (!Directory.Exists(runtimeRoot)) return;
                    foreach (var dir in Directory.GetDirectories(runtimeRoot))
                    {
                        TryAddDir(Path.Combine(dir, "bin"));
                        TryAddDir(Path.Combine(dir, "Contents", "Home", "bin"));
                    }
                }
                catch { }
            }

            void ScanJdkRoot(string root)
            {
                try
                {
                    if (!Directory.Exists(root)) return;

                    TryAddDir(Path.Combine(root, "bin"));
                    TryAddDir(Path.Combine(root, "Contents", "Home", "bin"));

                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        TryAddDir(Path.Combine(dir, "bin"));
                        TryAddDir(Path.Combine(dir, "Contents", "Home", "bin"));

                        try
                        {
                            foreach (var sub in Directory.GetDirectories(dir))
                            {
                                TryAddDir(Path.Combine(sub, "bin"));
                                TryAddDir(Path.Combine(sub, "Contents", "Home", "bin"));

                                try
                                {
                                    foreach (var sub2 in Directory.GetDirectories(sub))
                                    {
                                        TryAddDir(Path.Combine(sub2, "bin"));
                                        TryAddDir(Path.Combine(sub2, "Contents", "Home", "bin"));
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // ============ 1. NodePulse / 官方启动器 runtime ============
            try
            {
                ScanRuntimeRoot(Path.Combine(
                    Services.VersionScanner.MinecraftFolder, "runtime"));

                var appData = Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData);
                var localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                ScanRuntimeRoot(Path.Combine(appData, ".minecraft", "runtime"));
                ScanRuntimeRoot(Path.Combine(localAppData, ".minecraft", "runtime"));
            }
            catch { }

            // ============ 2. 系统 PATH ============
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    TryAddDir(dir.Trim());
                }
            }
            catch { }

            // ============ 3. JAVA_HOME ============
            try
            {
                var jh = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(jh))
                    TryAddDir(Path.Combine(jh, "bin"));
            }
            catch { }

            // ============ 4. 常见安装位置 ============
            var roots = new List<string>();

            string userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            string appDataDir = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            string localAppDir = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (Services.PlatformHelper.IsWindows)
            {
                roots.Add(Path.Combine(userProfile, ".jdks"));
                roots.Add(Path.Combine(userProfile, "scoop", "apps"));
                roots.Add(Path.Combine(localAppDir, "Programs"));
                roots.Add(Path.Combine(appDataDir, "Programs"));
                roots.Add(Path.Combine(localAppDir, "Packages"));

                roots.Add(@"C:\Program Files\Java");
                roots.Add(@"C:\Program Files (x86)\Java");
                roots.Add(@"C:\Program Files\Eclipse Adoptium");
                roots.Add(@"C:\Program Files\Microsoft\jdk");
                roots.Add(@"C:\Program Files\Zulu");
                roots.Add(@"C:\Program Files\BellSoft");
                roots.Add(@"C:\Program Files\Amazon Corretto");
                roots.Add(@"C:\Program Files\AdoptOpenJDK");
                roots.Add(@"C:\Program Files\OpenJDK");
                roots.Add(@"C:\Program Files\Semeru");
                roots.Add(@"C:\Program Files\Kona");
                roots.Add(@"C:\Program Files\RedHat");
                roots.Add(@"C:\Program Files\SapMachine");
                roots.Add(@"C:\Program Files\JetBrains");
                roots.Add(@"C:\Program Files\Common Files\Oracle\Java");
                roots.Add(@"C:\Program Files (x86)\Common Files\Oracle\Java");
                roots.Add(@"C:\Program Files\Android\Android Studio\jbr");
                roots.Add(@"D:\Program Files\Java");
                roots.Add(@"D:\Java");
                roots.Add(@"D:\Program Files\Eclipse Adoptium");
                roots.Add(@"E:\Java");
                roots.Add(@"E:\Program Files\Java");
                roots.Add(@"E:\Program Files\Eclipse Adoptium");

                roots.Add(Path.Combine(userProfile, ".hmcl", "java"));
                roots.Add(Path.Combine(userProfile, ".bakaXL", "java"));
                roots.Add(Path.Combine(appDataDir, ".minecraft", "runtime"));
                roots.Add(Path.Combine(localAppDir, ".minecraft", "runtime"));
            }
            else if (Services.PlatformHelper.IsLinux)
            {
                roots.AddRange(new[]
                {
                    "/usr/lib/jvm",
                    "/usr/java",
                    "/opt/java",
                    "/opt/jdk",
                    "/opt/jre",
                    "/usr/local/java",
                    "/usr/local/jdk",
                    "/usr/local/jre",
                });
                roots.Add(Path.Combine(userProfile, ".sdkman", "candidates", "java"));
                roots.Add(Path.Combine(userProfile, ".jdks"));
            }
            else
            {
                roots.AddRange(new[]
                {
                    "/Library/Java/JavaVirtualMachines",
                    "/System/Library/Java/JavaVirtualMachines",
                    "/opt/homebrew/opt",
                    "/usr/local/opt",
                });
                roots.Add(Path.Combine(userProfile, ".sdkman", "candidates", "java"));
                roots.Add(Path.Combine(userProfile, ".jdks"));
            }

            foreach (var root in roots)
                ScanJdkRoot(root);

            try
            {
                result.Sort((a, b) => b.MajorVersion.CompareTo(a.MajorVersion));
            }
            catch { }

            return result;
        }

        // ================================================================
        // 探测单个 java 可执行文件
        // ================================================================

        public static JavaInfo? Probe(string javaExePath)
        {
            try
            {
                if (!File.Exists(javaExePath)) return null;

                var dir = Path.GetDirectoryName(javaExePath);
                if (dir is null) return null;

                var probeExe = Path.Combine(dir,
                    Services.PlatformHelper.IsWindows ? "java.exe" : "java");
                if (!File.Exists(probeExe))
                    probeExe = javaExePath;

                var psi = new ProcessStartInfo
                {
                    FileName = probeExe,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p is null) return null;

                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(3000);

                var m = Regex.Match(stderr, @"version\s+""([^""]+)""");
                if (!m.Success) return null;

                var fullVer = m.Groups[1].Value;
                int major = 0;

                if (fullVer.StartsWith("1."))
                {
                    var parts = fullVer.Substring(2).Split('.');
                    if (parts.Length > 0 && int.TryParse(parts[0], out var v8))
                        major = v8;
                }
                else
                {
                    var parts = fullVer.Split('.');
                    if (parts.Length > 0 && int.TryParse(parts[0], out var v))
                        major = v;
                }

                if (major == 0) return null;

                var vendor = "Unknown";
                if (stderr.Contains("OpenJDK")) vendor = "OpenJDK";
                if (stderr.Contains("Adoptium")) vendor = "Adoptium";
                if (stderr.Contains("Zulu")) vendor = "Zulu";
                if (stderr.Contains("Corretto")) vendor = "Corretto";
                if (stderr.Contains("Microsoft")) vendor = "Microsoft";
                if (stderr.Contains("Oracle")) vendor = "Oracle";
                if (stderr.Contains("GraalVM")) vendor = "GraalVM";

                return new JavaInfo
                {
                    JavaPath = javaExePath,
                    MajorVersion = major,
                    FullVersion = fullVer,
                    Vendor = vendor
                };
            }
            catch { return null; }
        }

        // ================================================================
        // ★ 挑选最合适的 Java
        //   ★ 参数类型改成 IEnumerable<JavaInfo>，同时兼容 List / ObservableCollection
        // ================================================================

        public static JavaInfo? PickBest(IEnumerable<JavaInfo> all, int needed)
        {
            try
            {
                if (all is null) return null;

                var list = all as IList<JavaInfo> ?? all.ToList();
                if (list.Count == 0) return null;

                var exact = list.FirstOrDefault(j => j.MajorVersion == needed);
                if (exact is not null) return exact;

                var bigger = list
                    .Where(j => j.MajorVersion > needed)
                    .OrderBy(j => j.MajorVersion)
                    .FirstOrDefault();
                if (bigger is not null) return bigger;

                return list.OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            }
            catch { return null; }
        }
    }
}