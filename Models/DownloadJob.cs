using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Threading;

namespace NodePulse.Models
{
    public partial class DownloadJob : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string VersionId { get; set; } = "";
        public string VersionType { get; set; } = "";
        public string VersionName { get; set; } = "";

        /// <summary>本次下载产出的版本目录（取消时用于清理）</summary>
        public string? TargetVersionDir { get; set; }

        /// <summary>加载器图标</summary>
        [ObservableProperty] private Bitmap? _iconBitmap;

        /// <summary>
        /// ★ 图标大小按 VersionType 缩放（跟实例页一致）：
        ///   release / snapshot（原版）：36
        ///   OptiFine / Quilt：44
        ///   Forge：38
        ///   其他：40
        /// </summary>
        public double IconSize => VersionType switch
        {
            "OptiFine" => 44,
            "Quilt" => 44,
            "Forge" => 38,
            "NeoForge" => 40,
            "Fabric" => 40,
            "release" => 36,
            "snapshot" => 36,
            _ => 40
        };

        public string Title => $"安装 {VersionId}";

        [ObservableProperty] private string _stage = "准备中...";
        [ObservableProperty] private double _totalPercent;
        [ObservableProperty] private string _currentFile = "";
        [ObservableProperty] private double _filePercent;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilesRemaining))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingText))]
        [NotifyPropertyChangedFor(nameof(FilesDoneAll))]
        [NotifyPropertyChangedFor(nameof(FilesTotalAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAllText))]
        private int _filesDone;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilesRemaining))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingText))]
        [NotifyPropertyChangedFor(nameof(FilesDoneAll))]
        [NotifyPropertyChangedFor(nameof(FilesTotalAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAllText))]
        private int _filesTotal;

        [ObservableProperty] private double _filesPercent;
        [ObservableProperty] private string _downloadedSize = "";
        [ObservableProperty] private string _totalSize = "";
        [ObservableProperty] private double _bytesPercent;
        [ObservableProperty] private string _stageDescription = "准备中...";
        [ObservableProperty] private double _stagePercent;

        [ObservableProperty] private string _speedText = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateColor))]
        [NotifyPropertyChangedFor(nameof(IsRunning))]
        [NotifyPropertyChangedFor(nameof(IsCompleted))]
        [NotifyPropertyChangedFor(nameof(ShowCancel))]
        private DownloadJobState _state = DownloadJobState.Pending;

        [ObservableProperty] private bool _isExpanded = true;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FilesDoneAll))]
        [NotifyPropertyChangedFor(nameof(FilesTotalAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAll))]
        [NotifyPropertyChangedFor(nameof(FilesRemainingAllText))]
        private int _previousStagesFilesTotal;

        public string LastStageForFiles = "";
        public int LastStageTotalForFiles = 0;

        public ObservableCollection<ActiveDownloadTask> ActiveTasks { get; } = new();

        public CancellationTokenSource? Cts { get; set; }

        internal long LastSpeedBytes;
        internal DateTime LastSpeedTime = DateTime.UtcNow;
        internal DateTime LastByteChangeTime = DateTime.UtcNow;
        internal double SmoothedSpeed;

        public bool IsRunning => State == DownloadJobState.Running;
        public bool IsCompleted => State == DownloadJobState.Completed;
        public bool ShowCancel => State == DownloadJobState.Running ||
                                  State == DownloadJobState.Pending;

        public int FilesRemaining => Math.Max(0, FilesTotal - FilesDone);
        public string FilesRemainingText => $"剩余 {FilesRemaining} 个文件";

        public int FilesDoneAll => PreviousStagesFilesTotal + FilesDone;
        public int FilesTotalAll => PreviousStagesFilesTotal + FilesTotal;
        public int FilesRemainingAll => Math.Max(0, FilesTotalAll - FilesDoneAll);
        public string FilesRemainingAllText => $"剩余 {FilesRemainingAll} 个文件";

        public string StateColor => State switch
        {
            DownloadJobState.Running => "#2ECC71",
            DownloadJobState.Completed => "#2ECC71",
            DownloadJobState.Failed => "#D15468",
            DownloadJobState.Cancelled => "#888888",
            _ => "#AAAAAA"
        };
    }

    public enum DownloadJobState
    {
        Pending,
        Running,
        Completed,
        Cancelled,
        Failed
    }
}