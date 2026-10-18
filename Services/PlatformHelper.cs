using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NodePulse.Services
{
    public static class PlatformHelper
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>Java 可执行文件名：Windows 用 javaw（无控制台），其他用 java</summary>
        public static string GetJavaExecutableName()
            => IsWindows ? "javaw.exe" : "java";

        /// <summary>ClassPath 分隔符：Windows 用 ; 其他用 :</summary>
        public static string ClassPathSeparator => IsWindows ? ";" : ":";

        /// <summary>版本 JSON 的 rules[].os.name 值</summary>
        public static string RulesOsName
            => IsWindows ? "windows" : IsLinux ? "linux" : "osx";

        /// <summary>版本 JSON 的 rules[].os.arch 值</summary>
        public static string RulesOsArch
        {
            get
            {
                var arch = RuntimeInformation.OSArchitecture;
                return arch switch
                {
                    Architecture.X64 => "x86_64",
                    Architecture.X86 => "x86",
                    Architecture.Arm64 => "arm64",
                    Architecture.Arm => "arm",
                    _ => "x86_64"
                };
            }
        }

        /// <summary>Mojang Java 运行时清单里的平台键</summary>
        public static string JavaRuntimePlatformKey
        {
            get
            {
                var arch = RuntimeInformation.OSArchitecture;
                if (IsWindows)
                {
                    return arch switch
                    {
                        Architecture.X64 => "windows-x64",
                        Architecture.X86 => "windows-x86",
                        Architecture.Arm64 => "windows-arm64",
                        _ => "windows-x64"
                    };
                }
                if (IsLinux)
                {
                    return arch switch
                    {
                        Architecture.X64 => "linux",
                        Architecture.X86 => "linux-i386",
                        Architecture.Arm64 => "linux-arm64",
                        _ => "linux"
                    };
                }
                // macOS
                return arch switch
                {
                    Architecture.Arm64 => "mac-os-arm64",
                    _ => "mac-os"
                };
            }
        }

        /// <summary>natives 键名（lib.natives 字典里查）</summary>
        public static string NativesKey => RulesOsName;

        /// <summary>新式 natives 分类器前缀</summary>
        public static string NativesClassifierPrefix
        {
            get
            {
                if (IsWindows) return "natives-windows";
                if (IsLinux) return "natives-linux";
                return "natives-osx";
            }
        }

        /// <summary>Natives 库文件后缀</summary>
        public static string GetNativeLibraryExtension()
        {
            if (IsWindows) return ".dll";
            if (IsLinux) return ".so";
            if (IsMacOs) return ".dylib";
            return string.Empty;
        }

        /// <summary>系统默认 .minecraft 目录</summary>
        public static string GetDefaultMinecraftFolder()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (IsWindows)
                return Path.Combine(home, "AppData", "Roaming", ".minecraft");
            return Path.Combine(home, ".minecraft");
        }
    }
}