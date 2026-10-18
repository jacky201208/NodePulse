using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class MinecraftLauncher
    {
        // ================================================================
        // Forge installer 工具库黑名单
        // ================================================================
        private static readonly string[] ForgeForbiddenArtifacts =
        {
            "ForgeAutoRenamingTool",
            "installertools",
            "binarypatcher",
            "installer-tools",
        };

        private static bool IsForbiddenForgeArtifact(string? libName)
        {
            if (string.IsNullOrEmpty(libName)) return false;
            foreach (var f in ForgeForbiddenArtifacts)
                if (libName.Contains(f, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ================================================================
        // 本地提供的库（不校验、不下载）
        // OptiFine 的 JSON 里会出现 optifine:OptiFine / optifine:launchwrapper-of
        // 这两个库由 OptiFineInstaller 提前放进 libraries/，不需要校验
        // ================================================================
        private static bool IsLocallyProvidedLibrary(string? libName)
        {
            if (string.IsNullOrEmpty(libName)) return false;
            return libName.StartsWith("optifine:", StringComparison.OrdinalIgnoreCase);
        }

        // ================================================================
        // Forge / NeoForge 互斥（精确版）
        // ================================================================
        private static bool IsForgeOnlyArtifact(string? libName)
        {
            if (string.IsNullOrEmpty(libName)) return false;

            return libName.StartsWith("net.minecraftforge:fmlcore", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:fmlloader", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:fmlearlydisplay", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:mclanguage", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:javafmllanguage", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:lowcodelanguage", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.minecraftforge:forge:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNeoForgeOnlyArtifact(string? libName)
        {
            if (string.IsNullOrEmpty(libName)) return false;

            return libName.StartsWith("net.neoforged.fancymodloader:", StringComparison.OrdinalIgnoreCase)
                || libName.StartsWith("net.neoforged:forge:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCrossLoaderConflict(string? libName, bool isNeoForgeInstance)
        {
            if (string.IsNullOrEmpty(libName)) return false;

            if (isNeoForgeInstance)
                return IsForgeOnlyArtifact(libName);
            else
                return IsNeoForgeOnlyArtifact(libName);
        }

        private static bool IsNeoForgeInstance(VersionMetaFull meta, string versionId)
        {
            if (meta.libraries != null)
            {
                foreach (var lib in meta.libraries)
                {
                    var name = lib.name ?? "";
                    if (name.StartsWith("net.neoforged:", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            if (!string.IsNullOrEmpty(versionId) &&
                versionId.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        public static Process Launch(string versionId, Account? account = null)
        {
            return LaunchAsync(versionId, account).GetAwaiter().GetResult();
        }

        public static async Task<Process> LaunchAsync(string versionId, Account? account = null)
        {
            account ??= AccountService.GetCurrent();

            var settings = InstanceSettings.Load(versionId);
            var globalSettings = GlobalSettings.Load();
            string mcFolder = VersionScanner.MinecraftFolder;

            var meta = LoadAndMergeVersionMeta(mcFolder, versionId);
            EnsureModLoaderLibraries(meta, mcFolder, versionId);

            bool isNeoForge = IsNeoForgeInstance(meta, versionId);

            string javaPath = JavaLocator.FindJavaWithOverride(
                GetRequireJavaMajor(meta),
                settings.UseAutoJava ? null : settings.JavaPath
            );
            if (!File.Exists(javaPath))
                throw new Exception("找不到可用 Java 运行时，请检查实例设置");

            string gameDir = Path.Combine(mcFolder, "versions", versionId);
            string libDir = Path.Combine(mcFolder, "libraries");
            string assetsDir = Path.Combine(mcFolder, "assets");
            string nativesDir = Path.Combine(gameDir, $"{versionId}-natives");

            try
            {
                if (Directory.Exists(nativesDir))
                    Directory.Delete(nativesDir, recursive: true);
            }
            catch { }

            await ExtractNatives(meta, libDir, nativesDir);

            var cpList = new List<string>();
            var cpSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cpKeySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCp(string path, string? mavenName = null)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!File.Exists(path)) return;

                if (!string.IsNullOrEmpty(mavenName))
                {
                    var key = GetLibKeyFromName(mavenName);
                    if (!string.IsNullOrEmpty(key) && !cpKeySeen.Add(key))
                        return;
                }

                if (!cpSeen.Add(path)) return;
                cpList.Add(path);
            }

            bool isForgeBootstrap = !string.IsNullOrEmpty(meta.mainClass) &&
                meta.mainClass.Equals("cpw.mods.bootstraplauncher.BootstrapLauncher",
                    StringComparison.OrdinalIgnoreCase);

            string clientJar = Path.Combine(gameDir, $"{versionId}.jar");
            if (!File.Exists(clientJar) && !string.IsNullOrEmpty(meta.inheritsFrom))
            {
                string parentJar = Path.Combine(mcFolder, "versions",
                    meta.inheritsFrom, $"{meta.inheritsFrom}.jar");
                if (File.Exists(parentJar)) clientJar = parentJar;
            }
            AddCp(clientJar);

            foreach (var lib in meta.libraries ?? new List<LibraryMeta>())
            {
                if (!ShouldLoadLibrary(lib)) continue;
                if (!NativeClassifierMatches(lib.name)) continue;
                if (IsForbiddenForgeArtifact(lib.name)) continue;
                if (IsCrossLoaderConflict(lib.name, isNeoForge)) continue;

                if (lib.downloads?.artifact?.path != null)
                {
                    string libPath = Path.Combine(libDir,
                        lib.downloads.artifact.path.Replace('/', Path.DirectorySeparatorChar));
                    AddCp(libPath, lib.name);
                }
                else if (!string.IsNullOrEmpty(lib.name))
                {
                    string inferred = InferLibraryPath(libDir, lib.name);
                    AddCp(inferred, lib.name);
                }
            }

            string classPathSep = PlatformHelper.ClassPathSeparator;
            string classPath = string.Join(classPathSep, cpList);

            string playerName = account?.Username ?? settings.UserName ?? "Player";
            string playerUuid = !string.IsNullOrEmpty(account?.Uuid)
                ? account!.Uuid
                : "00000000-0000-0000-0000-000000000000";
            string accessToken = !string.IsNullOrEmpty(account?.AccessToken)
                ? account!.AccessToken
                : "0";
            string userType = account?.UserType ?? "legacy";
            bool isMicrosoft = account?.AccountType == "microsoft";

            var vars = new Dictionary<string, string>
            {
                {"${game_directory}", gameDir},
                {"${library_directory}", libDir},
                {"${assets_root}", assetsDir},
                {"${auth_player_name}", playerName},
                {"${auth_uuid}", playerUuid},
                {"${auth_access_token}", accessToken},
                {"${clientid}", isMicrosoft ? MicrosoftAuth.ClientId : ""},
                {"${auth_xuid}", ""},
                {"${user_type}", userType},
                {"${version_name}", versionId},
                {"${assets_index_name}", meta.assetIndex?.id ?? ""},
                {"${version_type}", "release"},
                {"${user_properties}", "{}"},
                {"${natives_directory}", nativesDir},
                {"${launcher_name}", "nodepulse"},
                {"${launcher_version}", "1.0"},
                {"${resolution_width}", settings.WindowWidth.ToString()},
                {"${resolution_height}", settings.WindowHeight.ToString()},
                {"${classpath}", classPath},
                {"${classpath_separator}", classPathSep},
                {"${primary_jar}", Path.Combine(gameDir, $"{versionId}.jar")},
            };

            int maxMem = settings.MaxMemoryMB > 0 ? settings.MaxMemoryMB : globalSettings.MaxMemoryMB;
            int minMem = settings.MinMemoryMB > 0 ? settings.MinMemoryMB : globalSettings.MinMemoryMB;
            if (maxMem <= 0) maxMem = 2048;
            if (minMem <= 0) minMem = 512;
            if (minMem < 256) minMem = 256;
            if (minMem > maxMem) minMem = maxMem;

            List<string> jvmArgs = new List<string>
            {
                $"-Xms{minMem}M",
                $"-Xmx{maxMem}M",
                "-Duser.language=zh",
                "-Duser.country=CN",
            };

            if (account != null &&
                account.AccountType == "yggdrasil" &&
                !string.IsNullOrEmpty(account.ApiRoot))
            {
                string? injectorJar = await EnsureAuthlibInjectorAsync();
                if (!string.IsNullOrEmpty(injectorJar))
                {
                    jvmArgs.Add($"-javaagent:{injectorJar}={account.ApiRoot}");
                    jvmArgs.Add("-Dauthlibinjector.debug");
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.JvmArgs))
            {
                jvmArgs.AddRange(SplitArgLine(settings.JvmArgs));
            }

            List<string> parsedJvm = new();
            if (meta.arguments != null && meta.arguments.jvm != null)
            {
                parsedJvm = ParseArgumentList(meta.arguments.jvm, vars, isJvmList: true);
            }

            var cleanedJvm = CleanJvmPairing(parsedJvm);

            bool jsonHasNativesPath = false;
            bool jsonHasLegacyClassPath = false;
            for (int i = 0; i < cleanedJvm.Count; i++)
            {
                var a = cleanedJvm[i];
                if (a.StartsWith("-Djava.library.path=", StringComparison.OrdinalIgnoreCase))
                    jsonHasNativesPath = true;
                else if (a.StartsWith("-DlegacyClassPath=", StringComparison.OrdinalIgnoreCase))
                    jsonHasLegacyClassPath = true;
            }

            jvmArgs.AddRange(cleanedJvm);

            if (isForgeBootstrap && !jsonHasLegacyClassPath)
            {
                jvmArgs.Add($"-DlegacyClassPath={classPath}");
            }

            if (!jsonHasNativesPath)
            {
                jvmArgs.Add($"-Djava.library.path={nativesDir}");
            }

            List<string> gameArgs = new List<string>();
            if (meta.arguments != null && meta.arguments.game != null)
            {
                var parsedGame = ParseArgumentList(meta.arguments.game, vars, isJvmList: false);

                var keySeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < parsedGame.Count; i++)
                {
                    string arg = parsedGame[i];

                    if (arg == "--demo") continue;
                    if (arg.StartsWith("--quickPlay", StringComparison.OrdinalIgnoreCase)) continue;

                    if (arg.StartsWith("--"))
                    {
                        bool hasValue = i + 1 < parsedGame.Count && !parsedGame[i + 1].StartsWith("--");
                        if (hasValue)
                        {
                            if (!keySeen.Add(arg))
                            {
                                i++;
                                continue;
                            }
                            gameArgs.Add(arg);
                            gameArgs.Add(parsedGame[i + 1]);
                            i++;
                            continue;
                        }
                        else
                        {
                            if (!keySeen.Add(arg)) continue;
                            gameArgs.Add(arg);
                            continue;
                        }
                    }
                    else
                    {
                        gameArgs.Add(arg);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(meta.minecraftArguments))
            {
                var oldParts = SplitArgLine(meta.minecraftArguments);
                foreach (var p in oldParts)
                {
                    string val = ReplaceVars(p, vars);
                    gameArgs.Add(val);
                }
            }

            if (!gameArgs.Any(a => a == "--assetsDir") && !string.IsNullOrEmpty(assetsDir))
            {
                gameArgs.Add("--assetsDir");
                gameArgs.Add(assetsDir);
            }
            if (!gameArgs.Any(a => a == "--assetIndex") && !string.IsNullOrEmpty(meta.assetIndex?.id))
            {
                gameArgs.Add("--assetIndex");
                gameArgs.Add(meta.assetIndex.id);
            }

            if (!string.IsNullOrWhiteSpace(settings.GameArgs))
            {
                gameArgs.AddRange(SplitArgLine(settings.GameArgs));
            }

            string mainClass = meta.mainClass;
            if (string.IsNullOrEmpty(mainClass))
                throw new Exception("版本 JSON 缺少 mainClass 字段，无法启动");

            var allArgs = new List<string>();
            allArgs.AddRange(jvmArgs);

            if (!isForgeBootstrap)
            {
                allArgs.Add("-cp");
                allArgs.Add(classPath);
            }

            allArgs.Add(mainClass);
            allArgs.AddRange(gameArgs);

            string argumentText = string.Join(" ", allArgs.Select(a =>
                a.Contains(' ') ? $"\"{a}\"" : a));
            string dumpFile = Path.Combine(AppContext.BaseDirectory, "last_launch_args.txt");
            string envDump =
                $"JAVA_HOME          = {Environment.GetEnvironmentVariable("JAVA_HOME")}{Environment.NewLine}" +
                $"_JAVA_OPTIONS      = {Environment.GetEnvironmentVariable("_JAVA_OPTIONS")}{Environment.NewLine}" +
                $"JAVA_TOOL_OPTIONS  = {Environment.GetEnvironmentVariable("JAVA_TOOL_OPTIONS")}{Environment.NewLine}" +
                $"JDK_JAVA_OPTIONS   = {Environment.GetEnvironmentVariable("JDK_JAVA_OPTIONS")}{Environment.NewLine}";

            File.WriteAllText(dumpFile,
                $"=== Platform ==={Environment.NewLine}{PlatformHelper.RulesOsName} / {PlatformHelper.RulesOsArch}{Environment.NewLine}" +
                $"=== Version ==={Environment.NewLine}{versionId}{Environment.NewLine}" +
                $"=== IsNeoForge ==={Environment.NewLine}{isNeoForge}{Environment.NewLine}" +
                $"=== Account ==={Environment.NewLine}{account?.DisplayText ?? "(none)"}{Environment.NewLine}" +
                $"=== InheritsFrom ==={Environment.NewLine}{meta.inheritsFrom ?? "(none)"}{Environment.NewLine}" +
                $"=== Java ==={Environment.NewLine}{javaPath}{Environment.NewLine}" +
                $"=== Environment ==={Environment.NewLine}{envDump}" +
                $"=== WorkingDirectory ==={Environment.NewLine}{gameDir}{Environment.NewLine}" +
                $"=== MainClass ==={Environment.NewLine}{mainClass}{Environment.NewLine}" +
                $"=== IsForgeBootstrap ==={Environment.NewLine}{isForgeBootstrap}{Environment.NewLine}" +
                $"=== Natives ==={Environment.NewLine}{nativesDir}{Environment.NewLine}" +
                $"=== Classpath ({cpList.Count} entries, sep='{classPathSep}') ==={Environment.NewLine}{classPath}{Environment.NewLine}" +
                $"=== Args ==={Environment.NewLine}{argumentText}{Environment.NewLine}");

            // ★ 从全局设置读游戏内语言
            string langCode = string.IsNullOrEmpty(globalSettings.GameLanguage)
                ? "zh_CN"
                : globalSettings.GameLanguage;
            EnsureGameLanguage(gameDir, langCode);

            string realJavaExe = javaPath;
            try
            {
                var dir = Path.GetDirectoryName(javaPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var javaExe = Path.Combine(dir, "java.exe");
                    if (File.Exists(javaExe))
                        realJavaExe = javaExe;
                }
            }
            catch { }

            string logFile = Path.Combine(AppContext.BaseDirectory, "mc-latest.log");
            try
            {
                File.WriteAllText(logFile,
                    $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} 启动 {versionId} ==={Environment.NewLine}" +
                    $"=== Java: {realJavaExe} ==={Environment.NewLine}{Environment.NewLine}");
            }
            catch { }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = realJavaExe,
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var a in allArgs)
                psi.ArgumentList.Add(a);

            var proc = new Process { StartInfo = psi };

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    try { File.AppendAllText(logFile, e.Data + Environment.NewLine); }
                    catch { }
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    try { File.AppendAllText(logFile, e.Data + Environment.NewLine); }
                    catch { }
                }
            };

            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                try
                {
                    string logLine = $"{Environment.NewLine}=== Process exited, code = {proc.ExitCode} ==={Environment.NewLine}";
                    File.AppendAllText(logFile, logLine);
                    File.AppendAllText(dumpFile, logLine);
                }
                catch { }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // ★ 累加启动次数并保存（进程成功起来后才算一次）
            try
            {
                settings.LaunchCount++;
                settings.Save(versionId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MinecraftLauncher] 保存启动次数失败：{ex.Message}");
            }

            return proc;
        }

        // ================================================================
        // jvm 参数配对清理
        // ================================================================

        private static List<string> CleanJvmPairing(List<string> input)
        {
            var needsValue = new HashSet<string>(StringComparer.Ordinal)
            {
                "--add-opens",
                "--add-exports",
                "--add-reads",
                "--add-modules",
                "-p",
                "--module-path"
            };

            var result = new List<string>();

            for (int i = 0; i < input.Count; i++)
            {
                string a = input[i];

                if (needsValue.Contains(a))
                {
                    if (i + 1 < input.Count && !input[i + 1].StartsWith("-"))
                    {
                        result.Add(a);
                        result.Add(input[i + 1]);
                        i++;
                    }
                    continue;
                }

                if (LooksLikeAddOpensValue(a))
                    continue;

                result.Add(a);
            }

            return result;
        }

        private static bool LooksLikeAddOpensValue(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.StartsWith("-")) return false;
            if (!s.Contains("/")) return false;
            if (!s.Contains("=")) return false;

            int slash = s.IndexOf('/');
            int eq = s.IndexOf('=');

            if (slash == 0 || eq < slash + 2) return false;
            if (s.IndexOf('=', eq + 1) >= 0) return false;

            return true;
        }

        // ================================================================
        // ★ 游戏内语言（读取全局设置，写 options.txt）
        // ================================================================

        /// <summary>
        /// 确保 options.txt 里的 lang 字段是用户指定的语言。
        /// 
        /// MC 的 options.txt 用的小写下划线格式（如 zh_cn、en_us）。
        /// </summary>
        private static void EnsureGameLanguage(string gameDir, string langCode)
        {
            try
            {
                if (string.IsNullOrEmpty(langCode))
                    langCode = "zh_CN";

                // 转成 MC 格式：小写 + 下划线
                // zh_CN → zh_cn
                // pt_BR → pt_br
                var mcLangCode = langCode.ToLowerInvariant().Replace('-', '_');
                var langLine = "lang:" + mcLangCode;

                if (!Directory.Exists(gameDir))
                    Directory.CreateDirectory(gameDir);

                var optionsPath = Path.Combine(gameDir, "options.txt");
                var utf8 = new System.Text.UTF8Encoding(false);

                if (!File.Exists(optionsPath))
                {
                    File.WriteAllText(optionsPath, langLine + "\n", utf8);
                    return;
                }

                string content;
                try { content = File.ReadAllText(optionsPath, utf8); }
                catch { return; }

                var lines = content.Split('\n');
                int existingLangIndex = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimEnd('\r');
                    if (trimmed.StartsWith("lang:", StringComparison.OrdinalIgnoreCase))
                    {
                        existingLangIndex = i;
                        break;
                    }
                }

                if (existingLangIndex >= 0)
                {
                    // 已有 lang 行：内容相同 → 跳过；不同 → 替换
                    var existingValue = lines[existingLangIndex].TrimEnd('\r')
                        .Substring("lang:".Length).Trim();

                    if (string.Equals(existingValue, mcLangCode, StringComparison.OrdinalIgnoreCase))
                        return;

                    lines[existingLangIndex] = langLine;
                    File.WriteAllText(optionsPath, string.Join('\n', lines), utf8);
                }
                else
                {
                    // 加一行
                    var sb = new System.Text.StringBuilder();
                    sb.Append(content);
                    if (content.Length > 0 && !content.EndsWith("\n"))
                        sb.Append('\n');
                    sb.Append(langLine);
                    sb.Append('\n');

                    File.WriteAllText(optionsPath, sb.ToString(), utf8);
                }
            }
            catch { }
        }

        // ================================================================
        // 启动前校验
        // ================================================================

        public static async Task ValidateAndRepairAsync(
            string versionId,
            IProgress<DownloadProgress>? progress = null)
        {
            string mcFolder = VersionScanner.MinecraftFolder;

            var meta = LoadAndMergeVersionMeta(mcFolder, versionId);
            EnsureModLoaderLibraries(meta, mcFolder, versionId);

            bool isNeoForge = IsNeoForgeInstance(meta, versionId);

            var missing = new List<DownloadTask>();

            progress?.Report(new DownloadProgress
            {
                Stage = "校验游戏文件",
                CurrentFile = "正在校验游戏文件...",
                Total = 0,
                Done = 0
            });

            string versionDir = Path.Combine(mcFolder, "versions", versionId);
            string clientJar = Path.Combine(versionDir, $"{versionId}.jar");
            string actualClientJar = clientJar;

            if (!File.Exists(clientJar) && !string.IsNullOrEmpty(meta.inheritsFrom))
            {
                string parentJar = Path.Combine(mcFolder, "versions",
                    meta.inheritsFrom, $"{meta.inheritsFrom}.jar");
                if (File.Exists(parentJar))
                    actualClientJar = parentJar;
            }

            if (!File.Exists(actualClientJar))
            {
                var url = meta.downloads?.client?.url;
                var size = meta.downloads?.client?.size ?? 0;
                if (!string.IsNullOrEmpty(url))
                {
                    missing.Add(new DownloadTask
                    {
                        Url = url,
                        Target = clientJar,
                        Name = $"{versionId} 客户端 jar",
                        MinSize = size > 0 ? size * 9 / 10 : 1024 * 1024
                    });
                }
            }

            string libDir = Path.Combine(mcFolder, "libraries");

            foreach (var lib in meta.libraries ?? new List<LibraryMeta>())
            {
                if (!ShouldLoadLibrary(lib)) continue;
                if (!NativeClassifierMatches(lib.name)) continue;
                if (IsForbiddenForgeArtifact(lib.name)) continue;
                if (IsCrossLoaderConflict(lib.name, isNeoForge)) continue;

                // ★ 跳过 OptiFine 本地提供的库
                if (IsLocallyProvidedLibrary(lib.name)) continue;

                string? libTarget = null;
                string? libUrl = null;
                long libSize = 0;

                if (lib.downloads?.artifact?.path != null)
                {
                    libTarget = Path.Combine(libDir,
                        lib.downloads.artifact.path.Replace('/', Path.DirectorySeparatorChar));
                    libUrl = lib.downloads.artifact.url;
                    libSize = lib.downloads.artifact.size;
                }
                else if (!string.IsNullOrEmpty(lib.name))
                {
                    var (relPath, url) = BuildMavenPath(lib.name, lib.url);
                    if (!string.IsNullOrEmpty(relPath))
                    {
                        libTarget = Path.Combine(libDir,
                            relPath.Replace('/', Path.DirectorySeparatorChar));
                        libUrl = url;
                        libSize = 0;
                    }
                }

                if (string.IsNullOrEmpty(libTarget) || string.IsNullOrEmpty(libUrl))
                    continue;

                bool needsDownload = false;

                if (!File.Exists(libTarget))
                {
                    needsDownload = true;
                }
                else
                {
                    var fi = new FileInfo(libTarget);
                    long minSize = libSize > 0 ? libSize * 9 / 10 : 20 * 1024;
                    if (fi.Length < minSize)
                        needsDownload = true;
                }

                if (needsDownload)
                {
                    missing.Add(new DownloadTask
                    {
                        Url = libUrl,
                        Target = libTarget,
                        Name = Path.GetFileName(libTarget),
                        MinSize = libSize > 0 ? libSize * 9 / 10 : 20 * 1024
                    });
                }
            }

            // ============================================================
            // assets：索引不存在 → 先同步下载索引，再扫描 objects
            // ============================================================
            try
            {
                string? assetIndexId = meta.assetIndex?.id;
                if (!string.IsNullOrEmpty(assetIndexId))
                {
                    var assetsRoot = Path.Combine(mcFolder, "assets");
                    var indexesDir = Path.Combine(assetsRoot, "indexes");
                    Directory.CreateDirectory(indexesDir);

                    var indexFile = Path.Combine(indexesDir, assetIndexId + ".json");

                    if (!File.Exists(indexFile))
                    {
                        var idxUrl = meta.assetIndex?.url;
                        if (!string.IsNullOrEmpty(idxUrl))
                        {
                            progress?.Report(new DownloadProgress
                            {
                                Stage = "校验游戏文件",
                                CurrentFile = $"下载 assets 索引 {assetIndexId}...",
                                Total = 0,
                                Done = 0
                            });

                            try
                            {
                                var idxJson = await GetStringWithFallbackAsync(idxUrl);
                                File.WriteAllText(indexFile, idxJson, Encoding.UTF8);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(
                                    $"[MinecraftLauncher] 下载 assets 索引失败：{ex.Message}");
                            }
                        }
                    }

                    if (File.Exists(indexFile))
                    {
                        var indexJson = File.ReadAllText(indexFile);
                        var indexRoot = JsonNode.Parse(indexJson)?.AsObject();
                        var objects = indexRoot?["objects"]?.AsObject();

                        if (objects != null)
                        {
                            string assetsObjectsDir = Path.Combine(assetsRoot, "objects");

                            foreach (var kv in objects)
                            {
                                var obj = kv.Value?.AsObject();
                                var hash = obj?["hash"]?.GetValue<string>();
                                var size = obj?["size"]?.GetValue<long>() ?? 0;
                                if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;

                                var subDir = hash.Substring(0, 2);
                                var target = Path.Combine(assetsObjectsDir, subDir, hash);

                                bool needAsset = false;
                                if (!File.Exists(target))
                                {
                                    needAsset = true;
                                }
                                else if (size > 0)
                                {
                                    var fi = new FileInfo(target);
                                    if (fi.Length != size)
                                        needAsset = true;
                                }

                                if (needAsset)
                                {
                                    missing.Add(new DownloadTask
                                    {
                                        Url = $"https://resources.download.minecraft.net/{subDir}/{hash}",
                                        Target = target,
                                        Name = hash,
                                        MinSize = size > 0 ? size : 1
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MinecraftLauncher] assets 处理失败：{ex.Message}");
            }

            if (missing.Count == 0)
            {
                progress?.Report(new DownloadProgress
                {
                    Stage = "校验游戏文件",
                    CurrentFile = "所有文件完整",
                    Total = 0,
                    Done = 0
                });
                return;
            }

            progress?.Report(new DownloadProgress
            {
                Stage = "校验游戏文件",
                CurrentFile = $"发现 {missing.Count} 个文件缺失，正在补全...",
                Total = missing.Count,
                Done = 0
            });

            await VersionDownloader.DownloadTasksAsync(missing, progress, "validate");
        }

        private static async Task<string> GetStringWithFallbackAsync(string officialUrl)
        {
            try
            {
                return await _http.GetStringAsync(officialUrl);
            }
            catch
            {
                var mirror = VersionDownloader.ToMirrorUrl(officialUrl);
                if (mirror != officialUrl)
                    return await _http.GetStringAsync(mirror);
                throw;
            }
        }

        // ================================================================
        // natives 过滤
        // ================================================================

        private static bool NativeClassifierMatches(string libName)
        {
            if (string.IsNullOrEmpty(libName)) return true;

            var idx = libName.LastIndexOf(":natives-", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return true;

            string classifier = libName.Substring(idx + 1);

            var osName = PlatformHelper.RulesOsName;
            var osArch = PlatformHelper.RulesOsArch;

            var expected = new List<string>();

            if (osName == "windows")
            {
                if (osArch == "x86_64")
                    expected.Add("natives-windows");
                else if (osArch == "x86")
                    expected.Add("natives-windows-x86");
                else if (osArch == "arm64")
                    expected.Add("natives-windows-arm64");
            }
            else if (osName == "linux")
            {
                if (osArch == "x86_64") expected.Add("natives-linux");
                else if (osArch == "arm64")
                {
                    expected.Add("natives-linux-arm64");
                    expected.Add("natives-linux-aarch64");
                }
            }
            else if (osName == "osx")
            {
                if (osArch == "x86_64") expected.Add("natives-macos");
                else if (osArch == "arm64") expected.Add("natives-macos-arm64");
            }

            return expected.Any(e => classifier.Equals(e, StringComparison.OrdinalIgnoreCase));
        }

        // ===================== authlib-injector =====================

        private static async Task<string?> EnsureAuthlibInjectorAsync()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "authlib-injector.jar");

            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 100_000)
                    return path;
            }
            catch { }

            try
            {
                using var http = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromMinutes(2) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("NodePulse/1.0");

                const string apiUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
                string apiJson = await http.GetStringAsync(apiUrl);

                using var doc = JsonDocument.Parse(apiJson);
                if (!doc.RootElement.TryGetProperty("download_url", out var urlElem))
                    return null;

                string? downloadUrl = urlElem.GetString();
                if (string.IsNullOrEmpty(downloadUrl))
                    return null;

                byte[] bytes = await http.GetByteArrayAsync(downloadUrl);
                if (bytes.Length < 100_000)
                    return null;

                await File.WriteAllBytesAsync(path, bytes);
                return path;
            }
            catch { return null; }
        }

        // ===================== 辅助方法 =====================

        private static void EnsureModLoaderLibraries(
            VersionMetaFull meta, string mcFolder, string versionId)
        {
            meta.libraries ??= new List<LibraryMeta>();

            var existingNames = new HashSet<string>(
                meta.libraries.Select(l => l.name),
                StringComparer.OrdinalIgnoreCase);

            string mainClass = meta.mainClass ?? "";

            if (mainClass.StartsWith("net.fabricmc", StringComparison.OrdinalIgnoreCase) ||
                mainClass.StartsWith("org.quiltmc", StringComparison.OrdinalIgnoreCase))
            {
                EnsureLibrariesByPrefix(meta, existingNames, mcFolder,
                    new[] { "net.fabricmc", "org.quiltmc" }, isNeoForge: false);
            }
            else if (mainClass.StartsWith("cpw.mods", StringComparison.OrdinalIgnoreCase) ||
                     mainClass.Contains("forge", StringComparison.OrdinalIgnoreCase) ||
                     mainClass.Contains("bootstraplauncher", StringComparison.OrdinalIgnoreCase))
            {
                bool isNeoForge = IsNeoForgeInstance(meta, versionId);

                if (isNeoForge)
                {
                    EnsureLibrariesByPrefix(meta, existingNames, mcFolder,
                        new[] { "net.neoforged", "cpw.mods",
                                "net.sf.jopt-simple", "com.electronwill.night-config",
                                "org.jline", "org.apache.maven", "net.jodah",
                                "net.minecrell" },
                        isNeoForge);
                }
                else
                {
                    EnsureLibrariesByPrefix(meta, existingNames, mcFolder,
                        new[] { "net.minecraftforge", "cpw.mods",
                                "net.sf.jopt-simple", "com.electronwill.night-config",
                                "org.jline", "org.apache.maven", "net.jodah",
                                "net.minecrell" },
                        isNeoForge);
                }

                EnsureLibrariesByPrefix(meta, existingNames, mcFolder,
                    new[] { "org.spongepowered", "org.ow2.asm", "com.google.guava",
                            "org.apache.commons", "org.apache.logging.log4j",
                            "org.slf4j", "org.openjdk.nashorn" },
                    isNeoForge);
            }
            else if (mainClass.Equals("net.minecraft.launchwrapper.Launch",
                StringComparison.OrdinalIgnoreCase))
            {
                // OptiFine 独立版：靠 EnsureVanillaLibrariesIfMissing 补
            }
        }

        private static void EnsureLibrariesByPrefix(
            VersionMetaFull meta,
            HashSet<string> existingNames,
            string mcFolder,
            string[] prefixes,
            bool isNeoForge)
        {
            string libRoot = Path.Combine(mcFolder, "libraries");

            var existingGAs = new HashSet<string>(
                meta.libraries.Select(l => GetGroupArtifactFromName(l.name)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var prefix in prefixes)
            {
                string prefixPath = prefix.Replace('.', Path.DirectorySeparatorChar);
                string prefixDir = Path.Combine(libRoot, prefixPath);
                if (!Directory.Exists(prefixDir)) continue;

                foreach (var jar in SafeEnumerateJars(prefixDir, 6))
                {
                    string rel = Path.GetRelativePath(libRoot, jar);
                    string[] parts = rel.Split(Path.DirectorySeparatorChar);

                    if (parts.Length < 4) continue;

                    string fileName = parts[parts.Length - 1];
                    string version = parts[parts.Length - 2];
                    string artifact = parts[parts.Length - 3];
                    string group = string.Join(".", parts.Take(parts.Length - 3));

                    string mavenName = $"{group}:{artifact}:{version}";
                    string ga = $"{group}:{artifact}";

                    if (IsForbiddenForgeArtifact(rel)) continue;
                    if (IsForbiddenForgeArtifact(mavenName)) continue;

                    if (IsCrossLoaderConflict(mavenName, isNeoForge)) continue;

                    if (existingNames.Contains(mavenName)) continue;
                    if (existingGAs.Contains(ga)) continue;

                    string fnLower = fileName.ToLowerInvariant();
                    if (fnLower.Contains("-sources") ||
                        fnLower.Contains("-javadoc") ||
                        fnLower.Contains("-natives-"))
                        continue;

                    string verLower = version.ToLowerInvariant();
                    if (verLower.Contains("alpha") ||
                        verLower.Contains("beta") ||
                        verLower.Contains("snapshot"))
                        continue;

                    meta.libraries.Add(new LibraryMeta
                    {
                        name = mavenName,
                        downloads = new LibraryDownloadsMeta
                        {
                            artifact = new ArtifactMeta
                            {
                                path = rel.Replace(Path.DirectorySeparatorChar, '/'),
                                url = "",
                                sha1 = "",
                                size = 0
                            }
                        }
                    });
                    existingNames.Add(mavenName);
                    existingGAs.Add(ga);
                }
            }
        }

        private static string GetGroupArtifactFromName(string mavenName)
        {
            if (string.IsNullOrEmpty(mavenName)) return "";
            var name = mavenName;
            var atIdx = name.IndexOf('@');
            if (atIdx >= 0) name = name.Substring(0, atIdx);
            var parts = name.Split(':');
            if (parts.Length < 2) return name;
            return $"{parts[0]}:{parts[1]}";
        }

        private static IEnumerable<string> SafeEnumerateJars(string dir, int maxDepth)
        {
            if (maxDepth < 0) yield break;

            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> subs = Array.Empty<string>();

            try { files = Directory.GetFiles(dir, "*.jar"); } catch { }
            try { subs = Directory.GetDirectories(dir); } catch { }

            foreach (var f in files)
                yield return f;

            foreach (var s in subs)
                foreach (var f in SafeEnumerateJars(s, maxDepth - 1))
                    yield return f;
        }

        private static string InferLibraryPath(string libRoot, string mavenName)
        {
            try
            {
                string name = mavenName;
                string ext = "jar";
                int at = name.IndexOf('@');
                if (at >= 0)
                {
                    ext = name.Substring(at + 1);
                    name = name.Substring(0, at);
                }

                var parts = name.Split(':');
                if (parts.Length < 3) return "";

                string group = parts[0];
                string artifact = parts[1];
                string version = parts[2];
                string classifier = parts.Length >= 4 ? parts[3] : "";

                string groupPath = group.Replace('.', Path.DirectorySeparatorChar);
                string fileName = string.IsNullOrEmpty(classifier)
                    ? $"{artifact}-{version}.{ext}"
                    : $"{artifact}-{version}-{classifier}.{ext}";

                return Path.Combine(libRoot, groupPath, artifact, version, fileName);
            }
            catch { return ""; }
        }

        private static (string path, string url) BuildMavenPath(string mavenName, string? baseUrl)
        {
            try
            {
                string name = mavenName;
                string ext = "jar";

                int at = name.IndexOf('@');
                if (at >= 0)
                {
                    ext = name.Substring(at + 1);
                    name = name.Substring(0, at);
                }

                var parts = name.Split(':');
                if (parts.Length < 3) return ("", "");

                string group = parts[0];
                string artifact = parts[1];
                string version = parts[2];
                string classifier = parts.Length >= 4 ? parts[3] : "";

                string groupPath = group.Replace('.', '/');
                string fileName = string.IsNullOrEmpty(classifier)
                    ? $"{artifact}-{version}.{ext}"
                    : $"{artifact}-{version}-{classifier}.{ext}";

                string path = $"{groupPath}/{artifact}/{version}/{fileName}";

                string baseClean = string.IsNullOrEmpty(baseUrl)
                    ? "https://libraries.minecraft.net/"
                    : baseUrl.TrimEnd('/') + "/";

                return (path, baseClean + path);
            }
            catch
            {
                return ("", "");
            }
        }

        // ================================================================
        // 版本链合并
        // ================================================================

        private static VersionMetaFull LoadAndMergeVersionMeta(string mcFolder, string versionId)
        {
            var chain = new List<VersionMetaFull>();
            string current = versionId;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                string jsonPath = Path.Combine(mcFolder, "versions", current, $"{current}.json");
                if (!File.Exists(jsonPath))
                    throw new FileNotFoundException($"版本 JSON 不存在：{jsonPath}");

                var json = File.ReadAllText(jsonPath);
                var meta = JsonSerializer.Deserialize<VersionMetaFull>(json)
                           ?? throw new Exception($"版本 JSON 解析失败：{jsonPath}");
                chain.Add(meta);

                current = meta.inheritsFrom ?? "";
            }

            VersionMetaFull result;
            if (chain.Count == 1)
            {
                result = chain[0];
            }
            else
            {
                result = MergeVersionChain(chain);
            }

            EnsureVanillaLibrariesIfMissing(result, mcFolder, versionId);

            return result;
        }

        private static VersionMetaFull MergeVersionChain(List<VersionMetaFull> chain)
        {
            var merged = new VersionMetaFull();

            for (int i = 0; i < chain.Count; i++)
            {
                var m = chain[i];
                if (string.IsNullOrEmpty(merged.id) && !string.IsNullOrEmpty(m.id))
                    merged.id = m.id;
                if (string.IsNullOrEmpty(merged.mainClass) && !string.IsNullOrEmpty(m.mainClass))
                    merged.mainClass = m.mainClass;
                if (string.IsNullOrEmpty(merged.minecraftArguments) && !string.IsNullOrEmpty(m.minecraftArguments))
                    merged.minecraftArguments = m.minecraftArguments;
                if (merged.assetIndex == null && m.assetIndex != null && !string.IsNullOrEmpty(m.assetIndex.id))
                    merged.assetIndex = m.assetIndex;
                if (merged.javaVersion == null && m.javaVersion != null && m.javaVersion.majorVersion > 0)
                    merged.javaVersion = m.javaVersion;
                if (merged.downloads == null && m.downloads != null)
                    merged.downloads = m.downloads;
            }
            merged.inheritsFrom = null;

            var allLibs = new List<LibraryMeta>();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].libraries != null)
                    allLibs.AddRange(chain[i].libraries!);
            }

            if (allLibs.Count > 0)
            {
                var keyLastIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < allLibs.Count; i++)
                {
                    var key = GetLibKeyFromName(allLibs[i].name);
                    if (!string.IsNullOrEmpty(key))
                        keyLastIndex[key] = i;
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var finalLibs = new List<LibraryMeta>();
                for (int i = 0; i < allLibs.Count; i++)
                {
                    var key = GetLibKeyFromName(allLibs[i].name);
                    if (string.IsNullOrEmpty(key))
                    {
                        finalLibs.Add(allLibs[i]);
                        continue;
                    }
                    if (keyLastIndex[key] != i) continue;
                    if (seen.Add(key)) finalLibs.Add(allLibs[i]);
                }
                merged.libraries = finalLibs;
            }

            var mergedJvm = new List<object>();
            var mergedGame = new List<object>();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var m = chain[i];
                if (m.arguments?.jvm != null) mergedJvm.AddRange(m.arguments.jvm);
                if (m.arguments?.game != null) mergedGame.AddRange(m.arguments.game);
            }

            if (mergedJvm.Count > 0 || mergedGame.Count > 0)
            {
                merged.arguments = new ArgumentsMeta
                {
                    jvm = mergedJvm,
                    game = mergedGame
                };
            }

            return merged;
        }

        // ================================================================
        // 运行时补齐原版库 + launchwrapper
        // ================================================================

        private static void EnsureVanillaLibrariesIfMissing(
            VersionMetaFull meta, string mcFolder, string versionId)
        {
            meta.libraries ??= new List<LibraryMeta>();

            string mainClass = meta.mainClass ?? "";

            bool isForgeBootstrap = mainClass.Equals(
                "cpw.mods.bootstraplauncher.BootstrapLauncher",
                StringComparison.OrdinalIgnoreCase);

            bool isLaunchwrapper = mainClass.Equals(
                "net.minecraft.launchwrapper.Launch",
                StringComparison.OrdinalIgnoreCase);

            if (!isForgeBootstrap && !isLaunchwrapper) return;

            var existingKeys = new HashSet<string>(
                meta.libraries.Select(l => GetLibKeyFromName(l.name)),
                StringComparer.OrdinalIgnoreCase);

            bool hasLog4jCore = existingKeys.Contains("org.apache.logging.log4j:log4j-core");
            bool hasJoptSimple = existingKeys.Contains("net.sf.jopt-simple:jopt-simple");

            if (!hasLog4jCore || !hasJoptSimple)
            {
                TryMergeFromDiskVanilla(meta, mcFolder, versionId, existingKeys);
                EnsureFallbackLibs(meta, existingKeys);
            }

            if (isLaunchwrapper)
            {
                EnsureLaunchwrapperLibs(meta, existingKeys);
            }
        }

        /// <summary>
        /// OptiFine 独立版需要 launchwrapper 本体。
        /// 如果 installer 生成的 JSON 里没写，硬编码补上。
        /// </summary>
        private static void EnsureLaunchwrapperLibs(
            VersionMetaFull meta, HashSet<string> existingKeys)
        {
            var fallbacks = new (string Name, string Path, string Url)[]
            {
                ("net.minecraft:launchwrapper:1.12",
                 "net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar",
                 "https://bmclapi2.bangbang93.com/maven/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar"),

                ("org.ow2.asm:asm-all:5.0.3",
                 "org/ow2/asm/asm-all/5.0.3/asm-all-5.0.3.jar",
                 "https://bmclapi2.bangbang93.com/maven/org/ow2/asm/asm-all/5.0.3/asm-all-5.0.3.jar"),
            };

            foreach (var (name, relPath, url) in fallbacks)
            {
                var key = GetLibKeyFromName(name);
                if (!existingKeys.Add(key)) continue;

                meta.libraries.Add(new LibraryMeta
                {
                    name = name,
                    downloads = new LibraryDownloadsMeta
                    {
                        artifact = new ArtifactMeta
                        {
                            path = relPath,
                            url = url,
                            sha1 = "",
                            size = 0
                        }
                    }
                });
            }
        }

        private static bool TryMergeFromDiskVanilla(
            VersionMetaFull meta,
            string mcFolder,
            string versionId,
            HashSet<string> existingKeys)
        {
            string? baseVersion = meta.inheritsFrom;
            if (string.IsNullOrEmpty(baseVersion))
            {
                var dashIdx = versionId.IndexOf('-');
                if (dashIdx > 0)
                    baseVersion = versionId.Substring(0, dashIdx);
            }
            if (string.IsNullOrEmpty(baseVersion)) return false;
            if (string.Equals(baseVersion, versionId, StringComparison.OrdinalIgnoreCase))
                return false;

            var direct = Path.Combine(mcFolder, "versions", baseVersion, baseVersion + ".json");
            string? vanillaJsonPath = File.Exists(direct) ? direct : null;

            if (vanillaJsonPath == null)
            {
                var versionsDir = Path.Combine(mcFolder, "versions");
                if (Directory.Exists(versionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(versionsDir))
                    {
                        var name = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(name)) continue;
                        if (name.StartsWith(".")) continue;
                        if (string.Equals(name, versionId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var json in Directory.GetFiles(dir, "*.json"))
                        {
                            try
                            {
                                var txt = File.ReadAllText(json);
                                var node = JsonNode.Parse(txt)?.AsObject();
                                if (node == null) continue;
                                var id = node["id"]?.GetValue<string>();
                                if (!string.Equals(id, baseVersion, StringComparison.OrdinalIgnoreCase))
                                    continue;
                                if (node["inheritsFrom"] != null) continue;

                                var mc = node["mainClass"]?.GetValue<string>() ?? "";
                                if (!mc.Equals("net.minecraft.client.main.Main",
                                        StringComparison.OrdinalIgnoreCase))
                                    continue;

                                vanillaJsonPath = json;
                                break;
                            }
                            catch { }
                        }
                        if (vanillaJsonPath != null) break;
                    }
                }
            }

            if (vanillaJsonPath == null || !File.Exists(vanillaJsonPath))
            {
                Debug.WriteLine(
                    $"[MinecraftLauncher] 未找到原版 {baseVersion} 的 JSON，跳过合并");
                return false;
            }

            try
            {
                var text = File.ReadAllText(vanillaJsonPath);
                var vanillaMeta = JsonSerializer.Deserialize<VersionMetaFull>(text);
                if (vanillaMeta?.libraries == null || vanillaMeta.libraries.Count == 0)
                    return false;

                int added = 0;
                foreach (var lib in vanillaMeta.libraries)
                {
                    if (IsForbiddenForgeArtifact(lib.name)) continue;

                    var key = GetLibKeyFromName(lib.name);
                    if (string.IsNullOrEmpty(key)) continue;
                    if (!existingKeys.Add(key)) continue;

                    meta.libraries.Add(lib);
                    added++;
                }

                Debug.WriteLine(
                    $"[MinecraftLauncher] 从 {vanillaJsonPath} 合并原版库：{added} 个");
                return added > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MinecraftLauncher] 合并原版库失败：{ex.Message}");
                return false;
            }
        }

        private static void EnsureFallbackLibs(
            VersionMetaFull meta, HashSet<string> existingKeys)
        {
            var fallbacks = new (string Name, string Path)[]
            {
                ("net.sf.jopt-simple:jopt-simple:5.0.4",
                 "net/sf/jopt-simple/jopt-simple/5.0.4/jopt-simple-5.0.4.jar"),

                ("org.apache.logging.log4j:log4j-api:2.17.1",
                 "org/apache/logging/log4j/log4j-api/2.17.1/log4j-api-2.17.1.jar"),

                ("org.apache.logging.log4j:log4j-core:2.17.1",
                 "org/apache/logging/log4j/log4j-core/2.17.1/log4j-core-2.17.1.jar"),
            };

            const string MavenBase = "https://bmclapi2.bangbang93.com/maven/";

            foreach (var (name, relPath) in fallbacks)
            {
                var key = GetLibKeyFromName(name);
                if (!existingKeys.Add(key)) continue;

                meta.libraries.Add(new LibraryMeta
                {
                    name = name,
                    downloads = new LibraryDownloadsMeta
                    {
                        artifact = new ArtifactMeta
                        {
                            path = relPath,
                            url = MavenBase + relPath,
                            sha1 = "",
                            size = 0
                        }
                    }
                });

                Debug.WriteLine($"[MinecraftLauncher] 硬编码兜底补库：{name}");
            }
        }

        private static string GetLibKeyFromName(string mavenName)
        {
            if (string.IsNullOrEmpty(mavenName)) return "";

            var name = mavenName;
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

        private static List<string> ParseArgumentList(List<object> argItems, Dictionary<string, string> vars, bool isJvmList)
        {
            List<string> result = new List<string>();
            foreach (var obj in argItems)
            {
                if (obj is not JsonElement elem) continue;

                if (elem.ValueKind == JsonValueKind.String)
                {
                    string val = elem.GetString()!;

                    if (isJvmList && (val == "-cp" || val == "-classpath" || val == "${classpath}"))
                        continue;

                    string replaced = ReplaceVars(val, vars);

                    if (isJvmList)
                        replaced = CleanJvmArg(replaced);

                    result.Add(replaced);
                    continue;
                }

                if (elem.ValueKind == JsonValueKind.Object)
                {
                    var ruleItem = JsonSerializer.Deserialize<ArgumentRuleItem>(elem);
                    if (ruleItem == null) continue;
                    if (!CheckArgumentRules(ruleItem.rules)) continue;

                    if (ruleItem.value.HasValue)
                    {
                        JsonElement valElem = ruleItem.value.Value;
                        if (valElem.ValueKind == JsonValueKind.String)
                        {
                            string s = valElem.GetString()!;

                            if (isJvmList && (s == "-cp" || s == "-classpath" || s == "${classpath}"))
                                continue;

                            string replaced = ReplaceVars(s, vars);

                            if (isJvmList)
                                replaced = CleanJvmArg(replaced);

                            result.Add(replaced);
                        }
                        else if (valElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var sub in valElem.EnumerateArray())
                            {
                                if (sub.ValueKind == JsonValueKind.String)
                                {
                                    string sv = sub.GetString()!;

                                    if (isJvmList && (sv == "-cp" || sv == "-classpath" || sv == "${classpath}"))
                                        continue;

                                    string replaced = ReplaceVars(sv, vars);

                                    if (isJvmList)
                                        replaced = CleanJvmArg(replaced);

                                    result.Add(replaced);
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        private static string CleanJvmArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return arg;
            if (!arg.StartsWith("-D")) return arg;

            int eq = arg.IndexOf('=');
            if (eq < 0) return arg;

            string key = arg.Substring(0, eq + 1);
            string value = arg.Substring(eq + 1);

            if (value.StartsWith("\"") || value.StartsWith("'")) return arg;

            return key + value.Trim();
        }

        private static bool CheckArgumentRules(List<ArgumentRule>? rules)
        {
            if (rules == null || rules.Count == 0) return true;

            var osName = PlatformHelper.RulesOsName;
            var osArch = PlatformHelper.RulesOsArch;

            bool allow = false;
            foreach (var r in rules)
            {
                bool matches = true;

                if (r.os != null)
                {
                    if (!string.IsNullOrEmpty(r.os.name) &&
                        !r.os.name.Equals(osName, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                    if (!string.IsNullOrEmpty(r.os.arch) &&
                        !r.os.arch.Equals(osArch, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                }

                if (r.features != null)
                {
                    foreach (var kv in r.features)
                    {
                        bool want = kv.Value;
                        bool has = kv.Key switch
                        {
                            "is_demo_user" => false,
                            "has_custom_resolution" => true,
                            "has_quick_plays_support" => false,
                            "is_quick_play_singleplayer" => false,
                            "is_quick_play_multiplayer" => false,
                            "is_quick_play_realms" => false,
                            _ => false
                        };
                        if (want != has) { matches = false; break; }
                    }
                }

                if (matches)
                {
                    if (r.action == "allow") allow = true;
                    if (r.action == "disallow") return false;
                }
            }
            return allow;
        }

        private static async Task ExtractNatives(VersionMetaFull meta, string libFolder, string outputNativesDir)
        {
            Directory.CreateDirectory(outputNativesDir);

            var jobs = new List<(string JarPath, List<string> Excludes)>();

            foreach (var lib in meta.libraries ?? new List<LibraryMeta>())
            {
                if (!ShouldLoadLibrary(lib)) continue;
                if (!NativeClassifierMatches(lib.name)) continue;
                if (IsForbiddenForgeArtifact(lib.name)) continue;

                var nativesPrefix = PlatformHelper.NativesClassifierPrefix;
                if (lib.name.Contains(":" + nativesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (lib.downloads?.artifact?.path != null)
                    {
                        string jarPath = Path.Combine(libFolder,
                            lib.downloads.artifact.path.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(jarPath))
                            jobs.Add((jarPath, lib.extract?.exclude ?? new List<string>()));
                    }
                    continue;
                }

                var nativesKey = PlatformHelper.NativesKey;
                if (lib.natives == null || !lib.natives.ContainsKey(nativesKey)) continue;
                if (lib.downloads?.classifiers == null) continue;

                string nativeClassifier = lib.natives[nativesKey];
                if (!lib.downloads.classifiers.TryGetValue(nativeClassifier, out var art)) continue;
                if (art.path == null) continue;

                string oldJarPath = Path.Combine(libFolder,
                    art.path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(oldJarPath)) continue;

                jobs.Add((oldJarPath, lib.extract?.exclude ?? new List<string>()));
            }

            foreach (var job in jobs)
                await ExtractNativeJar(job.JarPath, outputNativesDir, job.Excludes);
        }

        private static async Task ExtractNativeJar(string jarFile, string outDir, List<string> excludes)
        {
            try
            {
                using var za = ZipFile.OpenRead(jarFile);
                foreach (var entry in za.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    bool skip = excludes.Any(ex => entry.FullName.EndsWith(ex, StringComparison.OrdinalIgnoreCase));
                    if (skip) continue;

                    string fnLower = entry.Name.ToLowerInvariant();
                    bool isNative =
                        fnLower.EndsWith(".dll") ||
                        fnLower.EndsWith(".so") ||
                        fnLower.EndsWith(".dylib");

                    if (!isNative) continue;

                    string outPath = Path.Combine(outDir, entry.Name);
                    var dir = Path.GetDirectoryName(outPath);
                    if (string.IsNullOrEmpty(dir)) continue;
                    Directory.CreateDirectory(dir);

                    await using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
                    await using var es = entry.Open();
                    await es.CopyToAsync(fs);
                }
            }
            catch { }
        }

        private static bool ShouldLoadLibrary(LibraryMeta lib)
        {
            if (lib.rules == null || lib.rules.Count == 0) return true;

            var osName = PlatformHelper.RulesOsName;
            var osArch = PlatformHelper.RulesOsArch;

            bool allow = false;
            foreach (var r in lib.rules)
            {
                bool matches = true;

                if (r.os != null)
                {
                    if (!string.IsNullOrEmpty(r.os.name) &&
                        !r.os.name.Equals(osName, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                    if (!string.IsNullOrEmpty(r.os.arch) &&
                        !r.os.arch.Equals(osArch, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                }

                if (matches)
                {
                    if (r.action == "allow") allow = true;
                    if (r.action == "disallow") return false;
                }
            }
            return allow;
        }

        private static int GetRequireJavaMajor(VersionMetaFull meta)
        {
            if (meta.javaVersion != null && meta.javaVersion.majorVersion > 0)
                return meta.javaVersion.majorVersion;
            return 17;
        }

        private static string ReplaceVars(string input, Dictionary<string, string> vars)
        {
            string res = input;
            foreach (var kv in vars)
                res = res.Replace(kv.Key, kv.Value);
            return res;
        }

        private static List<string> SplitArgLine(string line)
        {
            return line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        #region Json 模型
        public class VersionMetaFull
        {
            public string id { get; set; } = string.Empty;
            public string mainClass { get; set; } = string.Empty;
            public string? inheritsFrom { get; set; }
            public AssetIndexMeta? assetIndex { get; set; }
            public string? minecraftArguments { get; set; }
            public ArgumentsMeta? arguments { get; set; }
            public JavaRequirement? javaVersion { get; set; }
            public List<LibraryMeta>? libraries { get; set; }
            public DownloadsMeta? downloads { get; set; }
        }

        public class AssetIndexMeta
        {
            public string id { get; set; } = string.Empty;
            public string url { get; set; } = string.Empty;
        }

        public class JavaRequirement
        {
            public int majorVersion { get; set; }
        }

        public class ArgumentsMeta
        {
            public List<object>? jvm { get; set; }
            public List<object>? game { get; set; }
        }

        public class ArgumentRuleItem
        {
            public List<ArgumentRule>? rules { get; set; }
            public JsonElement? value { get; set; }
        }

        public class ArgumentRule
        {
            public string action { get; set; } = string.Empty;
            public ArgumentRuleOs? os { get; set; }
            public Dictionary<string, bool>? features { get; set; }
        }

        public class ArgumentRuleOs
        {
            public string? name { get; set; }
            public string? arch { get; set; }
            public string? version { get; set; }
        }

        public class LibraryMeta
        {
            public string name { get; set; } = string.Empty;
            public string? url { get; set; }
            public LibraryDownloadsMeta? downloads { get; set; }
            public Dictionary<string, string>? natives { get; set; }
            public LibraryExtract? extract { get; set; }
            public List<LibraryRuleMeta>? rules { get; set; }
        }

        public class LibraryDownloadsMeta
        {
            public ArtifactMeta? artifact { get; set; }
            public Dictionary<string, ArtifactMeta>? classifiers { get; set; }
        }

        public class ArtifactMeta
        {
            public string? path { get; set; }
            public string url { get; set; } = string.Empty;
            public string sha1 { get; set; } = string.Empty;
            public long size { get; set; }
        }

        public class LibraryExtract
        {
            public List<string>? exclude { get; set; }
        }

        public class LibraryRuleMeta
        {
            public string action { get; set; } = string.Empty;
            public LibraryRuleOsMeta? os { get; set; }
            public Dictionary<string, bool>? features { get; set; }
        }

        public class LibraryRuleOsMeta
        {
            public string? name { get; set; }
            public string? arch { get; set; }
            public string? version { get; set; }
        }

        public class DownloadsMeta
        {
            public ArtifactMeta? client { get; set; }
            public ArtifactMeta? server { get; set; }
        }
        #endregion
    }
}