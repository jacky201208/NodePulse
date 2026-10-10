using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NodePulse.Models;

public partial class ModInfo : ObservableObject
{
    public string FileName { get; set; } = "";

    public string DisplayName
    {
        get
        {
            var name = FileName;
            if (name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                name = name[..^".disabled".Length];
            if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            return name;
        }
    }

    public string FullPath { get; set; } = "";

    public long SizeBytes { get; set; }

    public string SizeDisplay
    {
        get
        {
            if (SizeBytes < 1024) return $"{SizeBytes} B";
            if (SizeBytes < 1024 * 1024) return $"{SizeBytes / 1024.0:F1} KB";
            return $"{SizeBytes / 1024.0 / 1024:F1} MB";
        }
    }

    public bool IsEnabled => !FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool _isSelected;

    public static List<ModInfo> Scan(string modsFolder)
    {
        var result = new List<ModInfo>();

        try
        {
            if (!Directory.Exists(modsFolder))
                return result;

            foreach (var file in Directory.GetFiles(modsFolder))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var fileName = Path.GetFileName(file);

                var isJar = ext == ".jar";
                var isDisabled = ext == ".disabled" &&
                    fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

                if (!isJar && !isDisabled) continue;

                try
                {
                    var fi = new FileInfo(file);
                    result.Add(new ModInfo
                    {
                        FileName = fileName,
                        FullPath = file,
                        SizeBytes = fi.Length
                    });
                }
                catch { }
            }

            result = result
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { }

        return result;
    }
}