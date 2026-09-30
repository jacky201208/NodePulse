using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NodePulse.Services.Multiplayer
{
    /// <summary>
    /// 陶瓦联机服务：管理 terracotta 进程 + 调用 HTTP API。
    /// 
    /// 启动方式：terracotta.exe --hmcl2 &lt;handoff.json&gt;
    ///   ★ --hmcl2 让 terracotta 以 HMCL 兼容模式运行：
    ///     - 不自动打开浏览器
    ///     - 把 HTTP 端口写入 handoff.json（比解析 stdout 稳）
    /// 
    /// 关闭方式：/state/ide → /panic?peaceful=true → 等待进程退出 → Kill
    /// </summary>
    public sealed class TerracottaService : IDisposable
    {
        private Process? _process;
        private HttpClient? _http;
        private int _port;
        private bool _disposed;
        private string? _handoffPath;

        private const string ExpectedVersion = "0.4.2";
        private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        public bool IsRunning => _process != null && !_process.HasExited;
        public int Port => _port;
        public string? Version { get; private set; }

        public event Action<TerracottaState>? StateChanged;

        private CancellationTokenSource? _pollCts;

        // ================================================================
        // 启动
        // ================================================================

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (IsRunning) return;

            string? exePath = await TerracottaBinaryManager.EnsureExecutableAsync(null, ct);
            if (string.IsNullOrEmpty(exePath))
                throw new Exception("当前平台不支持陶瓦联机（安卓端请走 FFI 实现）");

            // ★ 生成 handoff 临时文件路径
            _handoffPath = Path.Combine(
                Path.GetTempPath(),
                $"nodepulse-terracotta-{Guid.NewGuid():N}.json");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // ★ 关键：--hmcl2 模式
            psi.ArgumentList.Add("--hmcl2");
            psi.ArgumentList.Add(_handoffPath);

            // 双保险：环境变量设置（--hmcl2 已经足够，这里无害）
            try
            {
                psi.EnvironmentVariables["BROWSER"] = "none";
            }
            catch { }

            _process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    Debug.WriteLine($"[Terracotta] {e.Data}");
            };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    Debug.WriteLine($"[Terracotta] {e.Data}");
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // ★ 等待 handoff.json 出现，读端口
            try
            {
                _port = await WaitForHandoffAsync(_process, _handoffPath, ct);
            }
            catch
            {
                Stop();
                throw;
            }

            _http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{_port}/"),
                Timeout = RequestTimeout
            };

            // ★ 校验 /meta
            var meta = await GetMetaAsync(ct);
            if (meta != null)
            {
                Version = meta.Version;
                Debug.WriteLine(
                    $"[Terracotta] 版本={meta.Version} OS={meta.TargetOs} Arch={meta.TargetArch}");

                if (!string.Equals(meta.TargetOs, "windows", StringComparison.OrdinalIgnoreCase)
                    && OperatingSystem.IsWindows())
                {
                    Debug.WriteLine(
                        $"[Terracotta] 警告：meta 报告的 OS 与当前平台不一致");
                }
            }

            _pollCts = new CancellationTokenSource();
            _ = PollStateLoopAsync(_pollCts.Token);
        }

        private static async Task<int> WaitForHandoffAsync(
            Process process, string handoffPath, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(HandoffTimeout);

            while (true)
            {
                timeoutCts.Token.ThrowIfCancellationRequested();

                if (File.Exists(handoffPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(handoffPath, timeoutCts.Token);
                        var handoff = JsonSerializer.Deserialize<TerracottaHandoff>(json);
                        if (handoff != null && handoff.Port > 0 && handoff.Port <= 65535)
                            return handoff.Port;
                    }
                    catch (IOException)
                    {
                        // 文件正在写入，等一下再试
                    }
                    catch (JsonException)
                    {
                        // JSON 还没写完，等一下再试
                    }
                }

                if (process.HasExited)
                    throw new Exception("陶瓦联机启动失败（进程提前退出）");

                try { await Task.Delay(50, timeoutCts.Token); }
                catch (OperationCanceledException)
                {
                    if (ct.IsCancellationRequested) throw;
                    throw new TimeoutException("启动陶瓦联机超时（20 秒内未收到端口）");
                }
            }
        }

        // ================================================================
        // 停止（优雅关闭）
        // ================================================================

        public void Stop()
        {
            _pollCts?.Cancel();

            // 1. 尝试优雅关闭
            if (_process != null && !_process.HasExited && _http != null)
            {
                try
                {
                    // /state/ide：回到等待状态
                    _http.GetStringAsync("state/ide").Wait(TimeSpan.FromSeconds(2));
                }
                catch { }

                try
                {
                    // /panic?peaceful=true：请求优雅退出
                    _http.GetStringAsync("panic?peaceful=true").Wait(TimeSpan.FromSeconds(2));
                }
                catch { }

                try
                {
                    _process.WaitForExit(3000);
                }
                catch { }
            }

            // 2. 兜底 Kill
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(2000);
                }
            }
            catch { }

            _process?.Dispose();
            _process = null;
            _http?.Dispose();
            _http = null;
            _port = 0;

            // 清理 handoff 临时文件
            if (!string.IsNullOrEmpty(_handoffPath))
            {
                try { if (File.Exists(_handoffPath)) File.Delete(_handoffPath); }
                catch { }
                _handoffPath = null;
            }
        }

        // ================================================================
        // API 调用
        // ================================================================

        public async Task<TerracottaMeta?> GetMetaAsync(CancellationToken ct = default)
        {
            EnsureHttp();
            try
            {
                var json = await _http!.GetStringAsync("meta", ct);
                return JsonSerializer.Deserialize<TerracottaMeta>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Terracotta] GetMeta 失败: {ex.Message}");
                return null;
            }
        }

        public async Task<TerracottaState?> GetStateAsync(CancellationToken ct = default)
        {
            EnsureHttp();
            try
            {
                var json = await _http!.GetStringAsync("state", ct);
                return JsonSerializer.Deserialize<TerracottaState>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Terracotta] GetState 失败: {ex.Message}");
                return null;
            }
        }

        public async Task SetWaitingAsync(CancellationToken ct = default)
        {
            EnsureHttp();
            await _http!.GetStringAsync("state/ide", ct);
        }

        /// <summary>
        /// 房主开始扫描/创建房间。
        /// ★ 只传 player，不传 room —— 房间码由 terracotta 生成。
        /// </summary>
        public async Task SetScanningAsync(
            string? player = null,
            CancellationToken ct = default)
        {
            EnsureHttp();
            var url = "state/scanning";
            if (!string.IsNullOrEmpty(player))
                url += $"?player={Uri.EscapeDataString(player)}";

            await _http!.GetStringAsync(url, ct);
        }

        /// <summary>
        /// 房客加入房间。
        /// </summary>
        public async Task SetGuestingAsync(
            string room,
            string? player = null,
            CancellationToken ct = default)
        {
            EnsureHttp();
            var url = $"state/guesting?room={Uri.EscapeDataString(room)}";
            if (!string.IsNullOrEmpty(player))
                url += $"&player={Uri.EscapeDataString(player)}";

            await _http!.GetStringAsync(url, ct);
        }

        // ================================================================
        // 状态轮询
        // ================================================================

        private async Task PollStateLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var state = await GetStateAsync(ct);
                    if (state != null)
                        StateChanged?.Invoke(state);
                }
                catch { }

                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void EnsureHttp()
        {
            if (_http == null)
                throw new InvalidOperationException(
                    "Terracotta 尚未启动，请先调用 StartAsync()");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}