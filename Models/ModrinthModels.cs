using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NodePulse.Models
{
    // ================================================================
    // 搜索响应
    // ================================================================

    public class ModrinthSearchResponse
    {
        [JsonPropertyName("hits")]
        public List<ModrinthSearchHit> Hits { get; set; } = new();

        [JsonPropertyName("offset")]
        public int Offset { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    public partial class ModrinthSearchHit : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("author")]
        public string Author { get; set; } = "";

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("follows")]
        public long Follows { get; set; }

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = "";

        [JsonPropertyName("latest_version")]
        public string? LatestVersion { get; set; }

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

        public string DownloadsText => FormatNumber(Downloads);
        public string AuthorText => $"作者：{Author}";

        private static string FormatNumber(long n)
        {
            if (n >= 100_000_000) return $"{n / 100_000_000.0:F1}亿";
            if (n >= 10_000) return $"{n / 10_000.0:F1}万";
            if (n >= 1_000) return $"{n / 1_000.0:F1}k";
            return n.ToString();
        }
    }

    // ================================================================
    // 项目详情
    // ================================================================

    public partial class ModrinthProject : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("followers")]
        public long Followers { get; set; }

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("project_type")]
        public string ProjectType { get; set; } = "";

        [JsonPropertyName("source_url")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("issues_url")]
        public string? IssuesUrl { get; set; }

        [JsonPropertyName("wiki_url")]
        public string? WikiUrl { get; set; }

        [JsonPropertyName("discord_url")]
        public string? DiscordUrl { get; set; }

        [JsonPropertyName("published")]
        public string Published { get; set; } = "";

        [JsonPropertyName("updated")]
        public string Updated { get; set; } = "";

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _iconBitmap;

        // ============ 显示辅助 ============

        public string DownloadsText => FormatNumber(Downloads);
        public string FollowersText => FormatNumber(Followers);

        /// <summary>纯文本介绍（去掉一部分 markdown 语法，UI 显示更干净）</summary>
        public string BodyPlainText
        {
            get
            {
                if (string.IsNullOrEmpty(Body)) return "";
                var text = Body;

                // 去掉图片语法 ![alt](url)
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"!\[[^\]]*\]\([^)]*\)", "");

                // 去掉链接语法 [text](url) → text
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"\[([^\]]+)\]\([^)]*\)", "$1");

                // 去掉加粗/斜体
                text = text.Replace("**", "").Replace("__", "");
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"(?<!\*)\*(?!\*)", "");

                // 去掉行首 # 号
                text = System.Text.RegularExpressions.Regex.Replace(
                    text, @"^\s*#+\s*", "", System.Text.RegularExpressions.RegexOptions.Multiline);

                // 去掉 html 标签
                text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");

                // 合并多个空行
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

                return text.Trim();
            }
        }

        private static string FormatNumber(long n)
        {
            if (n >= 100_000_000) return $"{n / 100_000_000.0:F1}亿";
            if (n >= 10_000) return $"{n / 10_000.0:F1}万";
            if (n >= 1_000) return $"{n / 1_000.0:F1}k";
            return n.ToString();
        }
    }

    // ================================================================
    // 版本信息
    // ================================================================

    public class ModrinthVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version_number")]
        public string VersionNumber { get; set; } = "";

        [JsonPropertyName("version_type")]
        public string VersionType { get; set; } = "";

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("date_published")]
        public string DatePublished { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ModrinthFile> Files { get; set; } = new();

        [JsonPropertyName("dependencies")]
        public List<ModrinthDependency> Dependencies { get; set; } = new();

        // ============ 显示辅助 ============

        public string VersionTypeText => VersionType switch
        {
            "release" => "正式版",
            "beta" => "Beta",
            "alpha" => "Alpha",
            _ => VersionType
        };

        public string VersionTypeColor => VersionType switch
        {
            "release" => "#4CAF50",
            "beta" => "#FF9800",
            "alpha" => "#F44336",
            _ => "#888888"
        };

        public string LoadersText => string.Join(" · ", Loaders);

        public string GameVersionsText => GameVersions.Count == 0
            ? "—"
            : string.Join(", ", GameVersions);

        public string PublishedText
        {
            get
            {
                if (DateTime.TryParse(DatePublished, out var dt))
                    return dt.ToLocalTime().ToString("yyyy-MM-dd");
                return DatePublished;
            }
        }

        public ModrinthFile? PrimaryFile =>
            Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
    }

    public class ModrinthFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string Filename { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("hashes")]
        public Dictionary<string, string> Hashes { get; set; } = new();

        public string SizeText
        {
            get
            {
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                if (Size < 1024L * 1024 * 1024) return $"{Size / 1024.0 / 1024:F1} MB";
                return $"{Size / 1024.0 / 1024 / 1024:F2} GB";
            }
        }
    }

    public class ModrinthDependency
    {
        [JsonPropertyName("version_id")]
        public string? VersionId { get; set; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; set; }

        [JsonPropertyName("dependency_type")]
        public string DependencyType { get; set; } = "";
    }

    // ================================================================
    // 下载任务
    // ================================================================

    public partial class ModDownloadJob : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public string ModName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string TargetPath { get; set; } = "";

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private double _percent;

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private long _downloadedBytes;

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private long _totalBytes;

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private string _state = "等待中";

        public string SizeText => TotalBytes > 0
            ? $"{FormatSize(DownloadedBytes)} / {FormatSize(TotalBytes)}"
            : FormatSize(DownloadedBytes);

        partial void OnDownloadedBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
        partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));

        private static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
        }
    }
}