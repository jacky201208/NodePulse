using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media.Imaging;
using NodePulse.Services;

namespace NodePulse.Models
{
    /// <summary>
    /// 一个已安装的 Minecraft 实例
    /// </summary>
    public class MinecraftInstance
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string MainClass { get; set; } = "";

        /// <summary>该版本 JSON 里声明的"最低 Java 主版本"（静态要求）</summary>
        public int JavaMajor { get; set; } = 8;

        public string InheritsFrom { get; set; } = "";
        public string AssetsIndex { get; set; } = "";
        public string ReleaseTime { get; set; } = "";
        public long TotalSize { get; set; } = 0;

        // ================================================================
        // ★ 新增：实际生效的 Java（手动指定 / 自动选择）
        // ================================================================

        /// <summary>实际生效的 Java 主版本。0 表示还没算出来</summary>
        public int EffectiveJavaMajor { get; set; } = 0;

        /// <summary>实际生效的 Java 可执行文件路径</summary>
        public string EffectiveJavaPath { get; set; } = "";

        /// <summary>是否是用户手动指定（否则是自动选择）</summary>
        public bool JavaIsManual { get; set; } = false;

        /// <summary>用于显示的主版本（优先 Effective，没有就用要求值）</summary>
        public int DisplayJavaMajor =>
            EffectiveJavaMajor > 0 ? EffectiveJavaMajor : JavaMajor;

        public string LoaderType { get; set; } = "vanilla";
        public Bitmap? IconBitmap { get; set; }

        public double BoxSize => 48;

        public double IconSize => LoaderType switch
        {
            "vanilla" => 36,
            "optifine" => 44,
            "quilt" => 44,
            "forge" => 38,
            _ => 40
        };

        public string DisplayText
        {
            get
            {
                int java = DisplayJavaMajor;
                var javaText = JavaIsManual ? $"Java {java}*" : $"Java {java}";
                return $"{Name,-24}  [{Type,-8}]  {javaText,-10}  {FormatSize(TotalSize)}";
            }
        }

        // ================================================================
        // 日志（写到 bin/Debug/net8.0/instance-scan.log）
        // ================================================================

        private static string GetLogPath()
            => Path.Combine(AppContext.BaseDirectory, "instance-scan.log");

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(GetLogPath(),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
            catch { }
            Debug.WriteLine($"[Instance] {msg}");
        }

        // ================================================================
        // 扫描
        // ================================================================

        public static List<MinecraftInstance> ScanAll()
        {
            // 每次扫描清空日志
            try { File.WriteAllText(GetLogPath(), ""); } catch { }

            var result = new List<MinecraftInstance>();
            var versionsDir = Path.Combine(
                VersionScanner.MinecraftFolder, "versions");

            Log($"扫描目录：{versionsDir}");

            if (!Directory.Exists(versionsDir))
            {
                Log("目录不存在，返回");
                return result;
            }

            // ★ 提前扫一遍所有 Java（走缓存，快）
            List<JavaInfo> allJava;
            try
            {
                allJava = JavaInfo.ScanAll();
                Log($"已发现 {allJava.Count} 个 Java");
            }
            catch (Exception ex)
            {
                allJava = new List<JavaInfo>();
                Log($"Java 扫描失败：{ex.Message}");
            }

            foreach (var dir in Directory.GetDirectories(versionsDir))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith(".")) continue;

                // 找 JSON：优先标准位置，否则目录里最大的 .json
                string? jsonPath = null;

                var standardJson = Path.Combine(dir, name + ".json");
                if (File.Exists(standardJson))
                {
                    jsonPath = standardJson;
                }
                else
                {
                    try
                    {
                        var candidates = Directory.GetFiles(dir, "*.json");
                        if (candidates.Length > 0)
                        {
                            jsonPath = candidates
                                .OrderByDescending(f =>
                                {
                                    try { return new FileInfo(f).Length; }
                                    catch { return 0L; }
                                })
                                .First();
                            Log($"{name}：标准 JSON 不存在，改用 {Path.GetFileName(jsonPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"{name}：查找 JSON 出错 {ex.Message}");
                    }
                }

                if (jsonPath == null)
                {
                    Log($"{name}：目录里没有 .json，跳过");
                    continue;
                }

                try
                {
                    var text = File.ReadAllText(jsonPath);
                    var root = JsonNode.Parse(text)?.AsObject();
                    if (root == null)
                    {
                        Log($"{name}：JSON 解析失败");
                        continue;
                    }

                    var inst = new MinecraftInstance
                    {
                        Name = name,
                        Id = GetString(root, "id") ?? name,
                        Type = GetString(root, "type") ?? "release",
                        MainClass = GetString(root, "mainClass") ?? "",
                        JavaMajor = GetJavaMajor(root),
                        InheritsFrom = GetString(root, "inheritsFrom") ?? "",
                        AssetsIndex = GetString(root, "assets") ?? "",
                        ReleaseTime = GetString(root, "releaseTime") ?? "",
                        TotalSize = GetDirectorySize(dir)
                    };

                    inst.LoaderType = DetectLoaderType(inst);

                    // ★ 计算实际生效的 Java
                    ComputeEffectiveJava(inst, allJava);

                    result.Add(inst);

                    var javaTag = inst.JavaIsManual ? "(手动)" : "(自动)";
                    Log($"{name}：成功 [{inst.LoaderType}] Java {inst.DisplayJavaMajor} {javaTag}");
                }
                catch (Exception ex)
                {
                    Log($"{name}：解析异常 {ex.GetType().Name}: {ex.Message}");
                }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Log($"扫描完成，共 {result.Count} 个实例");
            return result;
        }

        // ================================================================
        // ★ 计算实例实际生效的 Java
        // ================================================================

        private static void ComputeEffectiveJava(
            MinecraftInstance inst, List<JavaInfo> allJava)
        {
            try
            {
                var s = InstanceSettings.Load(inst.Name);

                // ---------- 1. 用户手动指定 ----------
                if (!s.UseAutoJava && !string.IsNullOrEmpty(s.JavaPath))
                {
                    // 先在缓存里找
                    var match = allJava.FirstOrDefault(j =>
                        string.Equals(j.JavaPath, s.JavaPath,
                            StringComparison.OrdinalIgnoreCase));

                    // 缓存里没有，但文件还在 → 现场 probe 一次
                    if (match == null && File.Exists(s.JavaPath))
                    {
                        try { match = JavaInfo.Probe(s.JavaPath); } catch { }
                    }

                    if (match != null)
                    {
                        inst.EffectiveJavaMajor = match.MajorVersion;
                        inst.EffectiveJavaPath = match.JavaPath;
                        inst.JavaIsManual = true;
                        return;
                    }

                    // 手动指定的 Java 已经不在磁盘上了 → 回退到自动
                }

                // ---------- 2. 自动选择 ----------
                var auto = JavaInfo.PickBest(allJava, inst.JavaMajor);
                if (auto != null)
                {
                    inst.EffectiveJavaMajor = auto.MajorVersion;
                    inst.EffectiveJavaPath = auto.JavaPath;
                    inst.JavaIsManual = false;
                }
                else
                {
                    // 系统一个 Java 都没有 → 显示版本要求值兜底
                    inst.EffectiveJavaMajor = inst.JavaMajor;
                    inst.EffectiveJavaPath = "";
                    inst.JavaIsManual = false;
                }
            }
            catch
            {
                inst.EffectiveJavaMajor = inst.JavaMajor;
                inst.EffectiveJavaPath = "";
                inst.JavaIsManual = false;
            }
        }

        // ================================================================
        // 图标加载 —— ★ 必须由 UI 线程调用
        // ================================================================

        public void LoadIcon()
        {
            try
            {
                var fileName = LoaderType switch
                {
                    "fabric" => "fabric.png",
                    "forge" => "forge.png",
                    "neoforge" => "neoforge.png",
                    "quilt" => "quilt.png",
                    "optifine" => "optifine.png",
                    _ => "vanilla.png"
                };

                var bmp = IconLoader.Load(fileName);
                if (bmp == null)
                    Log($"{Name}：图标 {fileName} 加载失败（返回 null）");

                IconBitmap = bmp;
            }
            catch (Exception ex)
            {
                Log($"{Name}：加载图标异常 {ex.Message}");
                IconBitmap = null;
            }
        }

        // ================================================================
        // 字段安全读取
        // ================================================================

        private static string? GetString(JsonObject root, string key)
        {
            try
            {
                var node = root[key];
                if (node == null) return null;
                if (node is JsonValue v && v.TryGetValue<string>(out var s))
                    return s;
                return null;
            }
            catch { return null; }
        }

        private static int GetJavaMajor(JsonObject root)
        {
            try
            {
                var jv = root["javaVersion"];
                if (jv is JsonObject o)
                {
                    var mv = o["majorVersion"];
                    if (mv is JsonValue v && v.TryGetValue<int>(out var i))
                        return i;
                    if (mv is JsonValue v2 && v2.TryGetValue<string>(out var s) &&
                        int.TryParse(s, out var i2))
                        return i2;
                }
            }
            catch { }
            return 8;
        }

        // ================================================================
        // 加载器识别 —— ★ 简化版，只用字符串判断
        // ================================================================

        private static string DetectLoaderType(MinecraftInstance inst)
        {
            var mainClass = inst.MainClass ?? "";
            var combined = $"{inst.Id} {inst.Name} {inst.InheritsFrom}".ToLowerInvariant();

            // 1. 优先按名称判断（最可靠，绝不会抛异常）
            if (combined.Contains("neoforge")) return "neoforge";
            if (combined.Contains("optifine")) return "optifine";
            if (combined.Contains("quilt")) return "quilt";
            if (combined.Contains("fabric")) return "fabric";
            if (combined.Contains("forge")) return "forge";

            // 2. 名称判断不出来，再看 mainClass
            if (mainClass.StartsWith("net.fabricmc", StringComparison.OrdinalIgnoreCase))
                return "fabric";
            if (mainClass.StartsWith("org.quiltmc", StringComparison.OrdinalIgnoreCase))
                return "quilt";
            if (mainClass.StartsWith("cpw.mods", StringComparison.OrdinalIgnoreCase) ||
                mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase))
                return "forge";

            return "vanilla";
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path).EnumerateFiles("*",
                    SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { return 0; }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }
    }
}