using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NodePulse.Models
{
    public class GlobalSettings
    {
        [JsonPropertyName("javaPath")]
        public string JavaPath { get; set; } = "";

        [JsonPropertyName("useAutoJava")]
        public bool UseAutoJava { get; set; } = true;

        [JsonPropertyName("maxMemoryMB")]
        public int MaxMemoryMB { get; set; } = 2048;

        [JsonPropertyName("minMemoryMB")]
        public int MinMemoryMB { get; set; } = 512;

        /// <summary>内存模式："auto" 自动分配 / "manual" 手动指定</summary>
        [JsonPropertyName("memoryMode")]
        public string MemoryMode { get; set; } = "auto";

        [JsonPropertyName("useMirror")]
        public bool UseMirror { get; set; } = true;

        [JsonPropertyName("minecraftFolder")]
        public string MinecraftFolder { get; set; } = "";

        /// <summary>界面语言</summary>
        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh_CN";

        /// <summary>★ 游戏内语言代码（如 zh_CN / en_US / ja_JP）</summary>
        [JsonPropertyName("gameLanguage")]
        public string GameLanguage { get; set; } = "zh_CN";

        [JsonPropertyName("lastVersion")]
        public string LastVersion { get; set; } = "";

        /// <summary>并发下载线程数（1-256）</summary>
        [JsonPropertyName("downloadThreadCount")]
        public int DownloadThreadCount { get; set; } = 64;

        /// <summary>每个文件失败重试次数（1-5）</summary>
        [JsonPropertyName("downloadRetryCount")]
        public int DownloadRetryCount { get; set; } = 2;

        /// <summary>下载限速（MB/s），0 表示不限</summary>
        [JsonPropertyName("limitDownloadSpeedMB")]
        public int LimitDownloadSpeedMB { get; set; } = 0;

        /// <summary>主题："dark" 或 "light"</summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "dark";

        /// <summary>自定义下载页上次使用的保存目录</summary>
        [JsonPropertyName("customDownloadSaveDir")]
        public string CustomDownloadSaveDir { get; set; } = "";

        // ============ 启动设置 ============

        [JsonPropertyName("checkFileIntegrity")]
        public bool CheckFileIntegrity { get; set; } = true;

        [JsonPropertyName("autoRepairMissing")]
        public bool AutoRepairMissing { get; set; } = true;

        [JsonPropertyName("minimizeOnLaunch")]
        public bool MinimizeOnLaunch { get; set; } = false;

        [JsonPropertyName("fullscreenLaunch")]
        public bool FullscreenLaunch { get; set; } = false;

        [JsonPropertyName("autoJoinServer")]
        public string AutoJoinServer { get; set; } = "";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        public static string GetSettingsPath()
        {
            try
            {
                return Path.Combine(AppContext.BaseDirectory, "global-settings.json");
            }
            catch
            {
                return "global-settings.json";
            }
        }

        public static GlobalSettings Load()
        {
            try
            {
                var path = GetSettingsPath();
                if (!File.Exists(path)) return new GlobalSettings();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<GlobalSettings>(json, JsonOpts)
                       ?? new GlobalSettings();
            }
            catch
            {
                return new GlobalSettings();
            }
        }

        public void Save()
        {
            try
            {
                var path = GetSettingsPath();
                var json = JsonSerializer.Serialize(this, JsonOpts);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}