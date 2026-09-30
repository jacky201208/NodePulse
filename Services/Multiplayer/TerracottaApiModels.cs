using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NodePulse.Services.Multiplayer
{
    /// <summary>
    /// /state 返回的 JSON 模型。
    /// </summary>
    public class TerracottaState
    {
        [JsonPropertyName("state")]
        public string State { get; set; } = "";

        [JsonPropertyName("room")]
        public string? Room { get; set; }

        [JsonPropertyName("type")]
        public int? ExceptionType { get; set; }

        [JsonPropertyName("profiles")]
        public List<TerracottaProfile>? Profiles { get; set; }
    }

    public class TerracottaProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("machine_id")]
        public string MachineId { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
    }

    /// <summary>
    /// /meta 返回的 JSON 模型。
    /// </summary>
    public class TerracottaMeta
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("target_os")]
        public string TargetOs { get; set; } = "";

        [JsonPropertyName("target_arch")]
        public string TargetArch { get; set; } = "";
    }

    /// <summary>
    /// --hmcl2 启动后写入的 handoff JSON。
    /// </summary>
    public class TerracottaHandoff
    {
        [JsonPropertyName("port")]
        public int Port { get; set; }
    }

    public static class TerracottaStateNames
    {
        public const string Waiting = "waiting";
        public const string HostScanning = "host-scanning";
        public const string HostStarting = "host-starting";
        public const string HostOk = "host-ok";
        public const string GuestConnecting = "guest-connecting";
        public const string GuestStarting = "guest-starting";
        public const string GuestOk = "guest-ok";
        public const string Exception = "exception";
    }
}