using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NodePulse.Services
{
    /// <summary>
    /// 统一缓存管理器。
    /// 
    /// 所有缓存文件存放在：
    ///     启动器目录/.NodePulse/cache/{category}/
    /// 
    /// 缓存键（key）会被 SHA1 哈希后作为文件名，避免 URL 里非法字符。
    /// </summary>
    public static class CacheManager
    {
        private static readonly object _lock = new();

        /// <summary>缓存根目录</summary>
        public static string GetCacheDir()
            => Path.Combine(AppContext.BaseDirectory, ".NodePulse", "cache");

        /// <summary>某类缓存的子目录（自动创建）</summary>
        public static string GetCategoryDir(string category)
        {
            var dir = Path.Combine(GetCacheDir(), category);
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        /// <summary>生成缓存文件的完整路径（key 的 SHA1 做文件名）</summary>
        public static string GetFilePath(string category, string key, string extension = ".bin")
        {
            if (string.IsNullOrEmpty(extension)) extension = ".bin";
            if (!extension.StartsWith(".")) extension = "." + extension;

            string hash = Sha1Hex(key);
            return Path.Combine(GetCategoryDir(category), hash + extension);
        }

        /// <summary>
        /// 读取缓存。文件不存在或超时返回 null。
        /// </summary>
        /// <param name="category">缓存类别（子目录名）</param>
        /// <param name="key">缓存键（任意字符串，会被 SHA1 哈希）</param>
        /// <param name="extension">文件扩展名，如 ".png"</param>
        /// <param name="ttl">有效期；null 表示永久有效</param>
        public static byte[]? Read(
            string category, string key,
            string extension = ".bin",
            TimeSpan? ttl = null)
        {
            try
            {
                var path = GetFilePath(category, key, extension);
                if (!File.Exists(path)) return null;

                if (ttl.HasValue)
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                    if (age > ttl.Value) return null;
                }

                return File.ReadAllBytes(path);
            }
            catch { return null; }
        }

        /// <summary>
        /// 写入缓存（原子写：先写 .tmp 再改名，防止并发读到半个文件）。
        /// </summary>
        public static void Write(
            string category, string key, byte[] data,
            string extension = ".bin")
        {
            if (data == null || data.Length == 0) return;

            try
            {
                var path = GetFilePath(category, key, extension);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tempPath = path + ".tmp";
                File.WriteAllBytes(tempPath, data);

                if (File.Exists(path))
                {
                    try { File.Delete(path); } catch { }
                }
                File.Move(tempPath, path);
            }
            catch { }
        }

        /// <summary>缓存是否存在</summary>
        public static bool Exists(
            string category, string key,
            string extension = ".bin",
            TimeSpan? ttl = null)
        {
            try
            {
                var path = GetFilePath(category, key, extension);
                if (!File.Exists(path)) return false;

                if (ttl.HasValue)
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
                    if (age > ttl.Value) return false;
                }

                return true;
            }
            catch { return false; }
        }

        /// <summary>删除某一类缓存</summary>
        public static void Clear(string category)
        {
            try
            {
                var dir = Path.Combine(GetCacheDir(), category);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }

        /// <summary>清空全部缓存</summary>
        public static void ClearAll()
        {
            try
            {
                var dir = GetCacheDir();
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { }
        }

        /// <summary>获取当前缓存总大小（字节）</summary>
        public static long GetTotalSize()
        {
            try
            {
                var dir = GetCacheDir();
                if (!Directory.Exists(dir)) return 0;

                long total = 0;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                return total;
            }
            catch { return 0; }
        }

        private static string Sha1Hex(string input)
        {
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}