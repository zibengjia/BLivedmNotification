using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Overlay.Pages;

public sealed partial class LogPage : Page
{
    private readonly Queue<string> _buffer = new();
    private const int MaxLines = 1000;
    private int _lineCount;

    public LogPage()
    {
        InitializeComponent();
    }

    public void Append(string message)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _buffer.Enqueue($"[{timestamp}] {message}");
            _lineCount++;

            while (_buffer.Count > MaxLines)
            {
                _buffer.Dequeue();
                _lineCount--;
            }

            LogContent.Text = string.Join("\n", _buffer);
            LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
            LineCount.Text = $"行数: {_lineCount}";
        });
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _buffer.Clear();
        _lineCount = 0;
        LogContent.Text = "";
        LineCount.Text = "行数: 0";
    }
}
