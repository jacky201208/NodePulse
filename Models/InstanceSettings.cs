using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodePulse.Models
{
    public class InstanceSettings
    {
        [JsonPropertyName("javaPath")]
        public string JavaPath { get; set; } = "";

        [JsonPropertyName("useAutoJava")]
        public bool UseAutoJava { get; set; } = true;

        [JsonPropertyName("maxMemoryMB")]
        public int MaxMemoryMB { get; set; } = 0;

        [JsonPropertyName("minMemoryMB")]
        public int MinMemoryMB { get; set; } = 0;

        [JsonPropertyName("jvmArgs")]
        public string JvmArgs { get; set; } = "";

        [JsonPropertyName("gameArgs")]
        public string GameArgs { get; set; } = "";

        [JsonPropertyName("windowWidth")]
        public int WindowWidth { get; set; } = 854;

        [JsonPropertyName("windowHeight")]
        public int WindowHeight { get; set; } = 480;

        [JsonPropertyName("fullscreen")]
        public bool Fullscreen { get; set; } = false;

        [JsonPropertyName("userName")]
        public string UserName { get; set; } = "Player";

        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh_CN";

        // ================================================================
        // ★ 新增字段（实例设置页使用）
        // ================================================================

        /// <summary>实例隔离：开启后 mods/config/saves 等都走 versions/&lt;name&gt;/</summary>
        [JsonPropertyName("instanceIsolation")]
        public bool InstanceIsolation { get; set; } = true;

        /// <summary>窗口标题模式："global" 跟随全局 / "custom" 自定义 / "default" 使用默认标题</summary>
        [JsonPropertyName("windowTitleMode")]
        public string WindowTitleMode { get; set; } = "global";

        [JsonPropertyName("customWindowTitle")]
        public string CustomWindowTitle { get; set; } = "";

        [JsonPropertyName("customInfo")]
        public string CustomInfo { get; set; } = "";

        /// <summary>内存模式："global" 跟随全局 / "auto" 自动配置 / "custom" 自定义</summary>
        [JsonPropertyName("memoryMode")]
        public string MemoryMode { get; set; } = "global";

        /// <summary>服务器验证方式："unrestricted" 无限制 / "online" 仅正版 / "offline" 仅离线</summary>
        [JsonPropertyName("serverValidationMode")]
        public string ServerValidationMode { get; set; } = "unrestricted";

        /// <summary>是否覆盖全局的自动加入服务器</summary>
        [JsonPropertyName("overrideAutoJoinServer")]
        public bool OverrideAutoJoinServer { get; set; } = false;

        [JsonPropertyName("autoJoinServerOverride")]
        public string AutoJoinServerOverride { get; set; } = "";

        /// <summary>该实例累计启动次数</summary>
        [JsonPropertyName("launchCount")]
        public int LaunchCount { get; set; } = 0;

        // ================================================================

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        public static InstanceSettings Load(string versionName)
        {
            try
            {
                var path = GetSettingsPath(versionName);
                if (!File.Exists(path)) return new InstanceSettings();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InstanceSettings>(json, JsonOpts)
                       ?? new InstanceSettings();
            }
            catch
            {
                return new InstanceSettings();
            }
        }

        public void Save(string versionName)
        {
            try
            {
                var path = GetSettingsPath(versionName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(this, JsonOpts);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static string GetSettingsPath(string versionName)
        {
            try
            {
                return Path.Combine(
                    Services.VersionScanner.MinecraftFolder,
                    "versions", versionName, "instance-settings.json");
            }
            catch
            {
                return "instance-settings.json";
            }
        }
    }
}