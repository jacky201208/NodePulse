using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.IO;

namespace NodePulse.Services
{
    public static class IconLoader
    {
        /// <summary>
        /// 从 Assets/Loaders/{fileName} 加载图标 Bitmap。
        /// 找不到返回 null。
        /// </summary>
        public static Bitmap? Load(string fileName)
        {
            try
            {
                var assemblyName = System.Reflection.Assembly
                    .GetExecutingAssembly().GetName().Name ?? "NodePulse";

                var uri = new Uri($"avares://{assemblyName}/Assets/Loaders/{fileName}");

                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    return new Bitmap(stream);
                }

                // fallback: 尝试从 exe 目录下的 Assets/Loaders/ 读
                var diskPath = Path.Combine(AppContext.BaseDirectory,
                    "Assets", "Loaders", fileName);
                if (File.Exists(diskPath))
                {
                    return new Bitmap(diskPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[IconLoader] 加载 {fileName} 失败: {ex.Message}");
            }

            return null;
        }
    }
}