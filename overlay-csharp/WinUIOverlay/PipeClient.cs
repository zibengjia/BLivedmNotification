using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Overlay;

public class PipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;

    public event Action<JsonElement>? OnMessage;
    public event Action<string>? OnError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public PipeClient(string pipeName = "BlivedmOverlay")
    {
        _pipeName = pipeName;
    }

    public async Task ConnectAsync(CancellationToken token = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = _cts.Token;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(ct);
                OnConnected?.Invoke();

                var buffer = new byte[4096];
                var leftover = new StringBuilder();

                while (_pipe.IsConnected && !ct.IsCancellationRequested)
                {
                    var bytesRead = await _pipe.ReadAsync(buffer, ct);
                    if (bytesRead == 0) break; // disconnected

                    var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    leftover.Append(chunk);

                    var fullText = leftover.ToString();
                    var lines = fullText.Split('\n');

                    // 除最后一段外都是完整行
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) continue;

                        try
                        {
                            var doc = JsonDocument.Parse(line);
                            OnMessage?.Invoke(doc.RootElement.Clone());
                        }
                        catch (JsonException ex)
                        {
                            OnError?.Invoke($"JSON parse error: {ex.Message}");
                        }
                    }

                    // 保留最后一个不完整段
                    leftover.Clear();
                    leftover.Append(lines[^1]);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                OnError?.Invoke($"Pipe error: {ex.Message}");
                OnDisconnected?.Invoke();
            }
            finally
            {
                _pipe?.Dispose();
                _pipe = null;
            }

            // 断线重连等待
            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(2000, ct);
            }
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}
