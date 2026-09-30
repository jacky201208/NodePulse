using CommunityToolkit.Mvvm.ComponentModel;

namespace NodePulse.Models
{
    public partial class ActiveDownloadTask : ObservableObject
    {
        public string Key { get; set; } = "";      // 唯一标识（用 target 路径）
        public string FileName { get; set; } = "";
        public string Url { get; set; } = "";

        [ObservableProperty] private double _percent;
        [ObservableProperty] private long _downloadedBytes;
        [ObservableProperty] private long _totalBytes;

        public string SizeText => TotalBytes > 0
            ? $"{FormatSize(DownloadedBytes)} / {FormatSize(TotalBytes)}"
            : FormatSize(DownloadedBytes);

        partial void OnDownloadedBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));
        partial void OnTotalBytesChanged(long value) => OnPropertyChanged(nameof(SizeText));

        private static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB";
        }
    }
}