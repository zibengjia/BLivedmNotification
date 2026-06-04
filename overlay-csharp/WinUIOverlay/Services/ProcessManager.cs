using System.Diagnostics;

namespace Overlay.Services;

/// <summary>
/// Manages the Python backend process lifecycle.
/// </summary>
public class ProcessManager : IDisposable
{
    private Process? _pythonProcess;

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

        if (string.IsNullOrEmpty(pythonCmd))
            pythonCmd = "python";

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
                LogMessage?.Invoke("后端进程已退出。");
                _pythonProcess?.Dispose();
                _pythonProcess = null;
                StatusChanged?.Invoke(false);
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
        if (_pythonProcess is { HasExited: false })
        {
            try
            {
                _pythonProcess.Kill(entireProcessTree: true);
                _pythonProcess.WaitForExit(2000);
                LogMessage?.Invoke("后端已停止。");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[ERR] 停止后端失败: {ex.Message}");
            }
            _pythonProcess?.Dispose();
            _pythonProcess = null;
        }
        StatusChanged?.Invoke(false);
    }

    public void Dispose()
    {
        Stop();
    }
}
