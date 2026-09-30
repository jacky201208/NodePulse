using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class VersionScanner
    {
        private static string? _mcFolder;

        public static string MinecraftFolder
        {
            get
            {
                try
                {
                    if (_mcFolder is not null) return _mcFolder;

                    var g = GlobalSettings.Load();
                    _mcFolder = !string.IsNullOrEmpty(g.MinecraftFolder)
                        ? g.MinecraftFolder
                        : Path.Combine(AppContext.BaseDirectory, ".minecraft");

                    return _mcFolder;
                }
                catch
                {
                    return Path.Combine(AppContext.BaseDirectory, ".minecraft");
                }
            }
            set { _mcFolder = value; }
        }

        public static void EnsureFolders()
        {
            try
            {
                if (!Directory.Exists(MinecraftFolder))
                    Directory.CreateDirectory(MinecraftFolder);

                var versions = Path.Combine(MinecraftFolder, "versions");
                if (!Directory.Exists(versions))
                    Directory.CreateDirectory(versions);
            }
            catch { }
        }

        public static List<string> GetInstalledVersions()
        {
            var result = new List<string>();
            try
            {
                EnsureFolders();
                var versionsDir = Path.Combine(MinecraftFolder, "versions");
                if (!Directory.Exists(versionsDir)) return result;

                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith(".")) continue;

                    var json = Path.Combine(dir, name + ".json");
                    if (!File.Exists(json)) continue;

                    result.Add(name);
                }

                result.Sort();
            }
            catch { }

            return result;
        }

        // ================================================================
        // ★ 查找用户已有的原版目录
        // ================================================================

        public static string? FindVanillaDir(string mcFolder, string mcVersion)
        {
            try
            {
                var versionsDir = Path.Combine(mcFolder, "versions");
                if (!Directory.Exists(versionsDir)) return null;

                foreach (var dir in Directory.GetDirectories(versionsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith(".")) continue;

                    string[] jsons;
                    try { jsons = Directory.GetFiles(dir, "*.json"); }
                    catch { continue; }

                    foreach (var jsonFile in jsons)
                    {
                        try
                        {
                            var text = File.ReadAllText(jsonFile);
                            var root = JsonNode.Parse(text)?.AsObject();
                            if (root == null) continue;

                            var id = root["id"]?.GetValue<string>();
                            if (!string.Equals(id, mcVersion, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // 必须是干净原版（不能有 inheritsFrom）
                            if (root["inheritsFrom"] != null) continue;

                            var mainClass = root["mainClass"]?.GetValue<string>() ?? "";
                            if (IsModLoaderMainClass(mainClass)) continue;

                            return name;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool IsModLoaderMainClass(string mainClass)
        {
            if (string.IsNullOrEmpty(mainClass)) return false;

            if (mainClass.StartsWith("net.fabricmc", StringComparison.OrdinalIgnoreCase)) return true;
            if (mainClass.StartsWith("org.quiltmc", StringComparison.OrdinalIgnoreCase)) return true;
            if (mainClass.StartsWith("cpw.mods", StringComparison.OrdinalIgnoreCase)) return true;
            if (mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase)) return true;
            if (mainClass.Contains("optifine", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // ================================================================
        // ★★★ 关键修复：合并原版 JSON 到目标版本
        //   1. libs：目标(loader)的在前、原版的在后；同 key 目标覆盖原版
        //   2. arguments：原版在前、目标在后
        //   3. 其他字段：目标优先
        // ================================================================

        public static void MergeWithVanilla(
            string mcFolder, string targetDir, string targetName, string mcVersion)
        {
            try
            {
                var targetJson = Path.Combine(targetDir, targetName + ".json");

                // 目标 JSON 不存在 → 从目录里挑最大的当源，并改成 targetName.json
                if (!File.Exists(targetJson))
                {
                    var jsons = Directory.GetFiles(targetDir, "*.json")
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .ToList();

                    if (jsons.Count == 0)
                        throw new Exception($"目标目录里没有任何 JSON：{targetDir}");

                    File.Move(jsons[0], targetJson, overwrite: true);

                    foreach (var f in jsons.Skip(1))
                        try { File.Delete(f); } catch { }
                }
                else
                {
                    // 清理目录里多余的 JSON
                    foreach (var f in Directory.GetFiles(targetDir, "*.json"))
                    {
                        if (string.Equals(f, targetJson, StringComparison.OrdinalIgnoreCase))
                            continue;
                        try { File.Delete(f); } catch { }
                    }
                }

                var targetText = File.ReadAllText(targetJson);
                var targetRoot = JsonNode.Parse(targetText)?.AsObject();
                if (targetRoot == null)
                    throw new Exception($"JSON 解析失败：{targetJson}");

                var vanillaJsonPath = Path.Combine(
                    mcFolder, "versions", mcVersion, mcVersion + ".json");

                if (File.Exists(vanillaJsonPath))
                {
                    try
                    {
                        var vanillaText = File.ReadAllText(vanillaJsonPath);
                        var vanillaRoot = JsonNode.Parse(vanillaText)?.AsObject();
                        if (vanillaRoot != null)
                            MergeVanillaInto(targetRoot, vanillaRoot);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VersionScanner] 合并原版 JSON 失败：{ex.Message}");
                    }
                }

                targetRoot.Remove("inheritsFrom");
                targetRoot["id"] = targetName;
                if (targetRoot["type"] == null) targetRoot["type"] = "release";

                File.WriteAllText(targetJson,
                    targetRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VersionScanner] 处理版本 JSON 失败：{ex.Message}");
            }

            // 复制原版 jar（如果目标没有）
            try
            {
                var vanillaJar = Path.Combine(
                    mcFolder, "versions", mcVersion, mcVersion + ".jar");
                var targetJar = Path.Combine(targetDir, targetName + ".jar");

                if (File.Exists(vanillaJar) && !File.Exists(targetJar))
                    File.Copy(vanillaJar, targetJar, overwrite: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VersionScanner] 复制原版 jar 失败：{ex.Message}");
            }
        }

        /// <summary>
        /// ★★★ 修复版：把 vanilla 合并进 loader 里
        ///   - libraries：loader 的库在前（优先），vanilla 独有库在后
        ///     同 group:artifact[:classifier] 的 key 由 loader 覆盖 vanilla
        ///   - arguments：vanilla 在前、loader 在后（loader 追加/覆盖）
        /// </summary>
        private static void MergeVanillaInto(JsonObject loader, JsonObject vanilla)
        {
            // ============================================================
            // 1. libraries：loader 的在前，vanilla 独有在后，按 key 去重
            // ============================================================
            var mergedLibs = new JsonArray();

            var loaderLibs = loader["libraries"] as JsonArray;
            var vanillaLibs = vanilla["libraries"] as JsonArray;

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1a. 先加 loader 的所有库
            if (loaderLibs != null)
            {
                foreach (var libNode in loaderLibs)
                {
                    var key = GetLibKey(libNode as JsonObject);
                    if (!string.IsNullOrEmpty(key))
                        seenKeys.Add(key);
                    mergedLibs.Add(libNode?.DeepClone());
                }
            }

            // 1b. 再加 vanilla 独有的库（跳过被 loader 覆盖的 key）
            if (vanillaLibs != null)
            {
                foreach (var libNode in vanillaLibs)
                {
                    var key = GetLibKey(libNode as JsonObject);
                    if (!string.IsNullOrEmpty(key) && !seenKeys.Add(key))
                        continue; // 已被 loader 覆盖
                    mergedLibs.Add(libNode?.DeepClone());
                }
            }

            loader["libraries"] = mergedLibs;

            // ============================================================
            // 2. arguments：vanilla 在前、loader 在后
            // ============================================================
            var mergedJvm = new JsonArray();
            var mergedGame = new JsonArray();

            if (vanilla["arguments"] is JsonObject vArgs)
            {
                if (vArgs["jvm"] is JsonArray vJvm)
                    foreach (var a in vJvm) mergedJvm.Add(a?.DeepClone());
                if (vArgs["game"] is JsonArray vGame)
                    foreach (var a in vGame) mergedGame.Add(a?.DeepClone());
            }

            if (loader["arguments"] is JsonObject lArgs)
            {
                if (lArgs["jvm"] is JsonArray lJvm)
                    foreach (var a in lJvm) mergedJvm.Add(a?.DeepClone());
                if (lArgs["game"] is JsonArray lGame)
                    foreach (var a in lGame) mergedGame.Add(a?.DeepClone());
            }

            loader["arguments"] = new JsonObject
            {
                ["jvm"] = mergedJvm,
                ["game"] = mergedGame
            };

            // ============================================================
            // 3. 其他标量字段：loader 优先；loader 没有的从 vanilla 补
            // ============================================================
            string[] carryFields =
            {
                "assets", "assetIndex", "downloads", "javaVersion",
                "minecraftArguments", "releaseTime", "time"
            };

            foreach (var field in carryFields)
            {
                if (loader[field] == null && vanilla[field] != null)
                    loader[field] = vanilla[field]!.DeepClone();
            }
        }

        /// <summary>
        /// 提取 maven name 的"去重键"：group:artifact[:classifier]
        /// 例如：
        ///   net.minecraftforge:forge:1.20.1-47.3.0   → net.minecraftforge:forge
        ///   org.lwjgl:lwjgl:3.3.1:natives-windows    → org.lwjgl:lwjgl:natives-windows
        ///   com.mojang:brigadier:1.0.18@jar           → com.mojang:brigadier
        /// </summary>
        private static string GetLibKey(JsonObject? lib)
        {
            if (lib == null) return "";
            var name = lib["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) return "";

            var atIdx = name.IndexOf('@');
            if (atIdx >= 0) name = name.Substring(0, atIdx);

            var parts = name.Split(':');
            if (parts.Length < 2) return name;

            var group = parts[0];
            var artifact = parts[1];
            var classifier = parts.Length >= 4 ? parts[3] : "";

            return string.IsNullOrEmpty(classifier)
                ? $"{group}:{artifact}"
                : $"{group}:{artifact}:{classifier}";
        }

        // ================================================================
        // 备份 / 还原原版目录
        // ================================================================

        public static string? BackupVanillaDir(string mcFolder, string mcVersion)
        {
            var vanillaDir = Path.Combine(mcFolder, "versions", mcVersion);
            if (!Directory.Exists(vanillaDir)) return null;

            var backupName = $".nodepulse-bak-{Guid.NewGuid():N}";
            var backupDir = Path.Combine(mcFolder, "versions", backupName);

            try
            {
                Directory.Move(vanillaDir, backupDir);
                return backupName;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"无法备份已有原版目录 {mcVersion}，请关闭占用它的程序后重试。\n" +
                    $"错误：{ex.Message}");
            }
        }

        public static void CleanupAndRestoreVanilla(
            string mcFolder, string mcVersion, string? backupName)
        {
            var vanillaDir = Path.Combine(mcFolder, "versions", mcVersion);

            try
            {
                if (Directory.Exists(vanillaDir))
                    Directory.Delete(vanillaDir, recursive: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VersionScanner] 清理临时原版目录失败：{ex.Message}");
            }

            if (backupName != null)
            {
                var backupDir = Path.Combine(mcFolder, "versions", backupName);
                if (Directory.Exists(backupDir))
                {
                    try { Directory.Move(backupDir, vanillaDir); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[VersionScanner] 还原原版目录失败：{ex.Message}。备份留在：{backupDir}");
                    }
                }
            }
        }
    }
}