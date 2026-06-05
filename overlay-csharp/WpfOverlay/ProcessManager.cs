using System.Diagnostics;
using System.IO;

namespace Overlay.Services;

/// <summary>
/// Manages the Python backend process lifecycle.
/// </summary>
public class ProcessManager : IDisposable
{
    private Process? _pythonProcess;
    private int _processGeneration;
    private volatile int _currentGeneration = -1;

    public bool IsRunning => _pythonProcess is { HasExited: false };
    public int? ProcessId => _pythonProcess?.Id;

    public event Action<bool>? StatusChanged;
    public event Action<string>? LogMessage;

    public void Start(string projectRoot, int roomId, string pythonCmd = "python")
    {
        if (IsRunning)
        {
            LogMessage?.Invoke("后端已在运行中。");
            return;
        }

        var mainPy = Path.Combine(projectRoot, "py_overlay", "main.py");
        if (!File.Exists(mainPy))
        {
            LogMessage?.Invoke($"[ERR] 找不到 {mainPy}");
            return;
        }

        pythonCmd = ResolvePythonCmd(projectRoot, pythonCmd);

        var psi = new ProcessStartInfo
        {
            FileName = pythonCmd,
            Arguments = $"\"{mainPy}\" --room {roomId}",
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        try
        {
            var myGen = Interlocked.Increment(ref _processGeneration);
            _currentGeneration = myGen;

            _pythonProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _pythonProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    LogMessage?.Invoke(e.Data);
            };
            _pythonProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    LogMessage?.Invoke($"[ERR] {e.Data}");
            };
            _pythonProcess.Exited += (_, _) =>
            {
                // Only handle if this generation's process is still current
                if (Interlocked.CompareExchange(ref _currentGeneration, -1, myGen) == myGen)
                {
                    if (Interlocked.Exchange(ref _pythonProcess, null) is { } oldProc)
                    {
                        oldProc.Dispose();
                        StatusChanged?.Invoke(false);
                    }
                }
            };

            _pythonProcess.Start();
            _pythonProcess.BeginOutputReadLine();
            _pythonProcess.BeginErrorReadLine();

            LogMessage?.Invoke($"后端已启动 (PID: {_pythonProcess.Id})");
            StatusChanged?.Invoke(true);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[ERR] 启动后端失败: {ex.Message}");
            _pythonProcess?.Dispose();
            _pythonProcess = null;
        }
    }

    public void Stop()
    {
        // Mark current generation as stale so Exited handler won't steal new process
        Interlocked.Exchange(ref _currentGeneration, -1);

        var proc = Interlocked.Exchange(ref _pythonProcess, null);
        if (proc is { HasExited: false })
        {
            try
            {
                LogMessage?.Invoke($"正在停止后端 (PID: {proc.Id}) ...");
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[ERR] 停止后端失败: {ex.Message}");
            }
        }

        proc?.Dispose();
        StatusChanged?.Invoke(false);
    }

    /// <summary>
    /// Resolve the Python executable path. Prefers venv python over system python.
    /// </summary>
    private static string ResolvePythonCmd(string projectRoot, string configured)
    {
        if (!string.IsNullOrEmpty(configured) && configured != "python")
            return configured;

        // Auto-detect uv / venv Python
        var venvPython = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
            return venvPython;

        venvPython = Path.Combine(projectRoot, ".venv", "bin", "python");
        if (File.Exists(venvPython))
            return venvPython;

        // Try uv
        var uvPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "uv.exe");
        if (File.Exists(uvPath))
            return uvPath;

        return "python";
    }

    public void Dispose()
    {
        Stop();
    }
}
