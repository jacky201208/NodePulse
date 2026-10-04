using System;
using System.Collections.Generic;
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
    /// 启动方式：terracotta.exe（不传 hmcl 参数，走 server/主控台模式）
    ///   - 稳定运行，HTTP 控制端口从 stdout 的 Rocket has launched from http://127.0.0.1:&lt;port&gt; 解析
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

        // 捕获进程输出，用于失败时给出可诊断的报错
        private readonly StringBuilder _capturedOut = new();
        private readonly StringBuilder _capturedErr = new();
        private int? _exitCode;

        // 从 stdout 解析出的 HTTP 控制端口
        private TaskCompletionSource<int>? _portTcs;
        private bool _portParsed;

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

            _capturedOut.Clear();
            _capturedErr.Clear();
            _exitCode = null;
            _portParsed = false;
            _portTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            string? exePath = await TerracottaBinaryManager.EnsureExecutableAsync(null, ct);
            if (string.IsNullOrEmpty(exePath))
                throw new Exception("当前平台不支持陶瓦联机（安卓端请走 FFI 实现）");

            // ★ 生成 & 预创建 handoff 文件（必须存在，否则 terracotta 会当未知参数直接退出）
            _handoffPath = Path.Combine(
                Path.GetTempPath(),
                $"nodepulse-terracotta-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(_handoffPath, "{}", ct);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // ★ 关键：HMCL 兼容模式（不弹浏览器、稳定运行、把 HTTP 端口写进 handoff 文件）。
            //   - Windows 二进制用 --hmcl2
            //   - Linux 二进制用 --hmcl
            //   所需参数由各平台刻定的二进制决定，二者行为一致。
            string hmclFlag = OperatingSystem.IsWindows() ? "--hmcl2" : "--hmcl";
            psi.ArgumentList.Add(hmclFlag);
            psi.ArgumentList.Add(_handoffPath);

            // 双保险：环境变量设置（这里无害）
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
                {
                    Debug.WriteLine($"[Terracotta] {e.Data}");
                    _capturedOut.AppendLine(e.Data);
                    TryParsePort(e.Data);
                }
            };
            _process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Debug.WriteLine($"[Terracotta] {e.Data}");
                    _capturedErr.AppendLine(e.Data);
                }
            };
            _process.Exited += (s, e) => _exitCode = _process?.ExitCode;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // ★ 等待 handoff 文件或 stdout 解析出端口
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

        // 等待 terracotta 上报 HTTP 控制端口。
        // 主信号：terracotta 会把端口写进 handoff 文件（{"port": xxxx}）
        // 辅信号：stdout 的 Rocket has launched from http://127.0.0.1:xxxx 或 secondary mode, port=xxxx
        private async Task<int> WaitForHandoffAsync(
            Process process, string handoffPath, CancellationToken ct)
        {
            // 后台轮询 handoff 文件，一旦读到有效端口就完成 _portTcs
#pragma warning disable CS4014
            _ = PollHandoffFileAsync(handoffPath);
#pragma warning restore CS4014

            try
            {
                var port = await _portTcs!.Task.WaitAsync(HandoffTimeout, ct);
                return port;
            }
            catch (TimeoutException)
            {
                // 未在超时时间内解析到端口。区分"进程已退出"与"仍在运行"
                if (process.HasExited)
                {
                    int code = process.ExitCode;
                    var err = _capturedErr.ToString().Trim();
                    var outp = _capturedOut.ToString().Trim();
                    var detail = new List<string>
                    {
                        $"退出码：{code}"
                    };
                    if (!string.IsNullOrEmpty(err))
                        detail.Add($"错误输出：{err}");
                    if (!string.IsNullOrEmpty(outp))
                        detail.Add($"标准输出：{outp}");
                    if (string.IsNullOrEmpty(err) && string.IsNullOrEmpty(outp))
                        detail.Add("（进程无任何输出）");

                    throw new Exception(
                        "陶瓦联机启动失败（进程提前退出）\n\n" +
                        string.Join("\n", detail) + "\n\n" +
                        "可能原因：\n" +
                        "· 已有残留的 terracotta 进程在运行（全局互斥锁被占用）\n" +
                        "· Linux 下缺少动态库依赖（如 libX11、libxcb、libgcc 等）\n" +
                        "· 已下载的 terracotta 与当前系统不兼容\n" +
                        "请关闭旧的 terracotta 进程后重试，并根据上方输出定位原因");
                }

                throw new TimeoutException("启动陶瓦联机超时（20 秒内未收到端口）");
            }
        }

        /// <summary>
        /// 轮询 handoff 文件，读到有效端口后交给 _portTcs（主信号）。
        /// 完成一次即终止；若磁盘上从未写入端口，则由 stdout 辅信号兜底。
        /// </summary>
        private async Task PollHandoffFileAsync(string handoffPath)
        {
            try
            {
                while (!_portParsed)
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(handoffPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("port", out var p)
                            && p.TryGetInt32(out int port)
                            && port > 0 && port <= 65535
                            && !_portParsed)
                        {
                            _portParsed = true;
                            _portTcs?.TrySetResult(port);
                            return;
                        }
                    }
                    catch (IOException) { }      // 写入中，重试
                    catch (System.Text.Json.JsonException) { } // 未写完，重试

                    await Task.Delay(80);
                }
            }
            catch { /* 已取消或出错，静默 */ }
        }

        /// <summary>
        /// 从 stdout 行解析 terracotta 的 HTTP 控制端口。
        /// server 模式：http://127.0.0.1:xxxx
        /// 兼容 hmcl 伴生模式：Running in secondary mode, port=xxxx
        /// </summary>
        private void TryParsePort(string line)
        {
            if (_portParsed || _portTcs == null || string.IsNullOrWhiteSpace(line))
                return;

            // server 模式标准输出：Rocket has launched from http://127.0.0.1:<port>
            var url = System.Text.RegularExpressions.Regex.Match(
                line, @"http://127\.0\.0\.1:(\d+)");
            if (url.Success)
            {
                if (int.TryParse(url.Groups[1].Value, out int p) && p > 0 && p <= 65535)
                {
                    _portParsed = true;
                    _portTcs.TrySetResult(p);
                }
                return;
            }

            // 兼容 hmcl 伴生模式：Running in secondary mode, port=xxxx
            if (line.IndexOf("secondary mode", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            int idx = line.LastIndexOf("port=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return;

            var digits = new string(
                line.Substring(idx + 5)
                    .TakeWhile(char.IsDigit)
                    .ToArray());

            if (int.TryParse(digits, out int port) && port > 0 && port <= 65535)
            {
                _portParsed = true;
                _portTcs.TrySetResult(port);
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

            CleanupHandoffFile();
        }

        private void CleanupHandoffFile()
        {
            if (string.IsNullOrEmpty(_handoffPath)) return;
            try { if (File.Exists(_handoffPath)) File.Delete(_handoffPath); }
            catch { }
            _handoffPath = null;
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