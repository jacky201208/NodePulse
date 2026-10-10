using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace NodePulse.Models
{
    // ================================================================
    // 搜索响应 (BMCLAPI /mc-mods)
    // ================================================================

    public class CfSearchResponse
    {
        [JsonPropertyName("data")]
        public List<CfSearchHit> Data { get; set; } = new();

        [JsonPropertyName("pagination")]
        public CfPagination? Pagination { get; set; }
    }

    public class CfSearchHit
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("authors")]
        public List<CfAuthor> Authors { get; set; } = new();

        [JsonPropertyName("logo")]
        public CfAttachment? Logo { get; set; }

        [JsonPropertyName("downloadCount")]
        public long DownloadCount { get; set; }

        [JsonPropertyName("dateModified")]
        public string DateModified { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<CfCategory> Categories { get; set; } = new();

        [JsonPropertyName("latestFiles")]
        public List<CfLatestFile> LatestFiles { get; set; } = new();

        [JsonPropertyName("gamePopularityRank")]
        public long GamePopularityRank { get; set; }
    }

    // ================================================================
    // 模组详情 (BMCLAPI /mc-addon/{addonId})
    // ================================================================

    public class CfModDetailResponse
    {
        [JsonPropertyName("data")]
        public CfModDetail? Data { get; set; }
    }

    public class CfModDetail
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("authors")]
        public List<CfAuthor> Authors { get; set; } = new();

        [JsonPropertyName("logo")]
        public CfAttachment? Logo { get; set; }

        [JsonPropertyName("downloadCount")]
        public long DownloadCount { get; set; }

        [JsonPropertyName("dateCreated")]
        public string DateCreated { get; set; } = "";

        [JsonPropertyName("dateModified")]
        public string DateModified { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<CfCategory> Categories { get; set; } = new();

        [JsonPropertyName("links")]
        public CfLinks? Links { get; set; }

        [JsonPropertyName("screenshots")]
        public List<CfAttachment> Screenshots { get; set; } = new();
    }

    // ================================================================
    // 文件列表 (BMCLAPI /mc-addon/{addonId}/files)
    // ================================================================

    public class CfFilesResponse
    {
        [JsonPropertyName("data")]
        public List<CfFile> Data { get; set; } = new();

        [JsonPropertyName("pagination")]
        public CfPagination? Pagination { get; set; }
    }

    public class CfFile
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = "";

        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("fileLength")]
        public long FileLength { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("gameVersions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("releaseType")]
        public int ReleaseType { get; set; }

        [JsonPropertyName("fileDate")]
        public string FileDate { get; set; } = "";

        [JsonPropertyName("dependencies")]
        public List<CfDependency> Dependencies { get; set; } = new();
    }

    // ================================================================
    // 下载链接响应 (BMCLAPI /mc-addon/{addonId}/files/{fileId}/download-url)
    // ================================================================

    public class CfDownloadUrlResponse
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = "";
    }

    // ================================================================
    // 分类列表 (BMCLAPI /categories?gameId=432)
    // ================================================================

    public class CfCategoryListResponse
    {
        [JsonPropertyName("data")]
        public List<CfCategoryClass> Data { get; set; } = new();
    }

    // ================================================================
    // 通用子模型
    // ================================================================

    public class CfPagination
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("pageSize")]
        public int PageSize { get; set; }

        [JsonPropertyName("resultCount")]
        public int ResultCount { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }
    }

    public class CfAuthor
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    public class CfAttachment
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("thumbnailUrl")]
        public string? ThumbnailUrl { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    public class CfCategory
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("classId")]
        public long ClassId { get; set; }

        [JsonPropertyName("isClass")]
        public bool? IsClass { get; set; }

        [JsonPropertyName("gameId")]
        public long GameId { get; set; }
    }

    public class CfCategoryClass
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<CfCategory> Categories { get; set; } = new();
    }

    public class CfLatestFile
    {
        [JsonPropertyName("gameVersions")]
        public List<string> GameVersions { get; set; } = new();
    }

    public class CfLinks
    {
        [JsonPropertyName("websiteUrl")]
        public string? WebsiteUrl { get; set; }

        [JsonPropertyName("sourceUrl")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("issuesUrl")]
        public string? IssuesUrl { get; set; }

        [JsonPropertyName("wikiUrl")]
        public string? WikiUrl { get; set; }
    }

    public class CfDependency
    {
        [JsonPropertyName("modId")]
        public long ModId { get; set; }

        [JsonPropertyName("type")]
        public int Type { get; set; }
    }

    // ================================================================
    // ★ CF → Modrinth 数据转换辅助方法
    // ================================================================

    public static class CurseForgeConverter
    {
        public static ModrinthSearchHit ToSearchHit(CfSearchHit cf)
        {
            return new ModrinthSearchHit
            {
                ProjectId = cf.Id.ToString(),
                Slug = cf.Slug,
                Title = cf.Name,
                Description = cf.Summary ?? "",
                Author = cf.Authors.FirstOrDefault()?.Name ?? "",
                IconUrl = cf.Logo?.ThumbnailUrl ?? cf.Logo?.Url,
                Downloads = cf.DownloadCount,
                Follows = 0,
                Categories = cf.Categories.Select(c => c.Name).ToList(),
                Loaders = new(),
                ProjectType = "mod",
                LatestVersion = "",
                IconBitmap = null
            };
        }

        public static ModrinthProject ToProject(CfModDetail cf)
        {
            return new ModrinthProject
            {
                Id = cf.Id.ToString(),
                Slug = cf.Slug,
                Title = cf.Name,
                Description = cf.Summary ?? "",
                Body = cf.Description ?? "",
                IconUrl = cf.Logo?.ThumbnailUrl ?? cf.Logo?.Url,
                Downloads = cf.DownloadCount,
                Followers = 0,
                Categories = cf.Categories.Select(c => c.Name).ToList(),
                Loaders = new(),
                GameVersions = new(),
                ProjectType = "mod",
                Published = cf.DateCreated,
                Updated = cf.DateModified,
                SourceUrl = cf.Links?.SourceUrl,
                IssuesUrl = cf.Links?.IssuesUrl,
                WikiUrl = cf.Links?.WikiUrl,
                DiscordUrl = null,
                IconBitmap = null
            };
        }

        public static ModrinthVersion ToVersion(CfFile cf)
        {
            return new ModrinthVersion
            {
                Id = cf.Id.ToString(),
                ProjectId = "",
                Name = cf.DisplayName,
                VersionNumber = cf.DisplayName,
                VersionType = cf.ReleaseType switch { 1 => "release", 2 => "beta", 3 => "alpha", _ => "release" },
                GameVersions = cf.GameVersions,
                Loaders = new(),
                Downloads = 0,
                DatePublished = cf.FileDate,
                Files = new List<ModrinthFile>
                {
                    new ModrinthFile
                    {
                        Url = cf.DownloadUrl ?? "",
                        Filename = cf.FileName,
                        Size = cf.FileLength,
                        Primary = true
                    }
                },
                Dependencies = new()
            };
        }
    }
}