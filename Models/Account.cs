using System;
using System.Collections.Generic;

namespace NodePulse.Models
{
    public class ProfileInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class Account
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Username { get; set; } = "";
        public string Uuid { get; set; } = "";
        public string AccessToken { get; set; } = "0";
        public string ClientToken { get; set; } = "";
        public string UserType { get; set; } = "legacy";
        public string AccountType { get; set; } = "offline";
        public string ApiRoot { get; set; } = "";
        public string ServerName { get; set; } = "";
        public string LastLoginTime { get; set; } = "";

        /// <summary>
        /// 本地皮肤路径：
        ///   ""            → 默认（Mojang 官方皮肤 / Steve）
        ///   "__alex__"    → 默认 Alex
        ///   绝对路径      → 用户导入的皮肤文件
        /// </summary>
        public string LocalSkinPath { get; set; } = "";

        public List<ProfileInfo> AvailableProfiles { get; set; } = new();

        public string TypeLabel => AccountType switch
        {
            "offline" => "离线",
            "yggdrasil" => string.IsNullOrEmpty(ServerName) ? "外置" : $"外置 · {ServerName}",
            "microsoft" => "正版",
            _ => AccountType
        };

        /// <summary>用户名首字母（用于头像兜底/列表展示），不参与序列化。</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Initial
        {
            get
            {
                var n = Username?.Trim() ?? "";
                return n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "?";
            }
        }

        public string DisplayText => $"{Username}  [{TypeLabel}]";

        public bool IsYggdrasil => AccountType == "yggdrasil";
    }

    public class AccountStore
    {
        public List<Account> Accounts { get; set; } = new();
        public string CurrentAccountId { get; set; } = "";
    }
}