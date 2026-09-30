using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse.Services.Multiplayer
{
    /// <summary>
    /// Linker 进程服务。
    /// 
    /// ★ Linker 需要管理员权限（创建虚拟网卡），启动时会弹 UAC。
    /// ★ 提权进程无法用 Process.Kill()，需要用 taskkill 关闭。
    /// </summary>
    public sealed class LinkerService : IDisposable
    {
        public const int DefaultWebPort = 1804;

        private Process? _process;
        private bool _disposed;

        public bool IsRunning { get; private set; }
        public int WebPort { get; private set; } = DefaultWebPort;
        public string WebUrl => $"http://127.0.0.1:{WebPort}";

        /// <summary>日志事件（内部产生的消息，如启动/停止/错误）</summary>
        public event Action<string>? LogReceived;

        private void Log(string msg)
        {
            try { LogReceived?.Invoke(msg); } catch { }
            Debug.WriteLine($"[LinkerService] {msg}");
        }

        // ================================================================
        // 启动（带 UAC 提权）
        // ================================================================

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (IsRunning) return;

            // 先尝试结束可能残留的 linker.exe
            KillExistingProcesses();

            // ★ 从启动器目录下的 .NodePulse/linker/ 读取（不打包进 Assets）
            // 找不到会抛异常，异常消息包含详细下载指引
            string exePath = await LinkerBinaryManager.EnsureLinkerAsync(null, ct);

            var workDir = Path.GetDirectoryName(exePath)!;

            // ★ 关键：UseShellExecute = true + Verb = "runas" 触发 UAC
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workDir,
                UseShellExecute = true,
                Verb = "runas"   // 请求管理员权限
            };

            try
            {
                _process = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception wex)
                when (wex.NativeErrorCode == 1223)
            {
                // 用户在 UAC 对话框点了"否"
                throw new Exception("用户取消了管理员权限请求，Linker 无法启动");
            }
            catch (Exception ex)
            {
                throw new Exception($"启动 Linker 失败：{ex.Message}");
            }

            if (_process == null)
                throw new Exception("启动 Linker 失败：进程对象为空");

            IsRunning = true;
            Log($"Linker 进程已启动 (PID={_process.Id})，等待 Web UI 就绪...");

            // 等待 Web UI 就绪（最多 30 秒）
            bool ready = await WaitForWebUiAsync(WebPort, 30, ct);

            if (!ready)
            {
                // 检查进程是否还活着
                if (!IsLinkerProcessAlive())
                {
                    IsRunning = false;
                    throw new Exception(
                        "Linker 启动失败：进程已退出。\n" +
                        "可能原因：UAC 被拒绝、wintun.dll 缺失、或端口被占用。");
                }
                // 进程活着但 Web 没响应，可能是端口不对，仍认为是成功启动
                Log($"警告：端口 {WebPort} 未响应，但进程仍在运行");
            }
            else
            {
                Log($"Linker Web UI 已就绪：{WebUrl}");
            }
        }

        private static async Task<bool> WaitForWebUiAsync(
            int port, int timeoutSec, CancellationToken ct)
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSec))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var resp = await http.GetAsync(
                        $"http://127.0.0.1:{port}/", ct);
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch { }

                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { throw; }
            }

            return false;
        }

        // ================================================================
        // 停止（用 taskkill 关闭提权进程）
        // ================================================================

        public void Stop()
        {
            KillExistingProcesses();

            _process?.Dispose();
            _process = null;

            if (IsRunning)
                Log("Linker 已停止");

            IsRunning = false;
        }

        /// <summary>
        /// 强制结束所有 linker 进程（包括提权的）。
        /// </summary>
        private void KillExistingProcesses()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // 提权进程 Process.Kill() 会失败，用 taskkill
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = "/F /IM linker.exe /T",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var p = Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                else
                {
                    // Linux/macOS：直接 kill
                    var psi = new ProcessStartInfo
                    {
                        FileName = "pkill",
                        Arguments = "-f linker",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    p?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Log($"结束旧进程失败：{ex.Message}");
            }
        }

        private static bool IsLinkerProcessAlive()
        {
            try
            {
                var procs = Process.GetProcessesByName("linker");
                return procs.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // ================================================================
        // 打开 Web 管理界面
        // ================================================================

        public void OpenWebUi()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = WebUrl,
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", WebUrl);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", WebUrl);
                }
            }
            catch (Exception ex)
            {
                Log($"打开浏览器失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}