using System.Text.Json;

namespace Overlay;

public class DanmakuEngine
{
    private readonly List<DanmakuItem> _items = new();
    private readonly object _lock = new();
    private readonly Config _config;
    private int _nextTrack;
    private long _lastFrameTicks;

    public int ActiveCount { get; private set; }

    public DanmakuEngine(Config config)
    {
        _config = config;
        _lastFrameTicks = Environment.TickCount64;
    }

    public int ScreenHeight { get; set; } = 1080;
    public int ScreenWidth { get; set; } = 1920;

    /// <summary>
    /// Delegate for accurate text width measurement using DirectWrite.
    /// Set by OverlayForm after renderer initialization.
    /// </summary>
    public Func<string, float, float>? MeasureTextWidth { get; set; }

    /// <summary>
    /// Reserved space at top for SuperChat items, so they don't overlap danmaku tracks.
    /// </summary>
    private const float ScZoneHeight = 50f;

    /// <summary>
    /// Process incoming JSON message from pipe
    /// </summary>
    public void HandleMessage(JsonElement msg)
    {
        if (!msg.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();

        switch (type)
        {
            case "danmaku":
                AddDanmaku(msg);
                break;
            case "super_chat":
                AddSuperChat(msg);
                break;
        }
    }

    private void AddDanmaku(JsonElement msg)
    {
        var uname = GetString(msg, "uname");
        var text = GetString(msg, "msg");
        var color = GetInt(msg, "color", 0xFFFFFF);
        // Use config font size for all danmaku — renderer uses a fixed format
        var fontSize = _config.Danmaku.FontSize;
        var timestamp = GetLong(msg, "timestamp", 0);

        var displayText = $"{uname}: {text}";

        var item = new DanmakuItem
        {
            Text = displayText,
            Uname = uname,
            Color = color,
            FontSize = fontSize,
            Speed = _config.Danmaku.Speed,
            Opacity = _config.Danmaku.Opacity,
            IsSC = false,
            TextWidth = MeasureTextWidth?.Invoke(displayText, fontSize)
                       ?? displayText.Length * fontSize * 0.55f,
            CreatedAt = timestamp,
        };

        lock (_lock)
        {
            AssignTrack(item);
            item.X = ScreenWidth;
            _items.Add(item);
        }
    }

    private void AddSuperChat(JsonElement msg)
    {
        var uname = GetString(msg, "uname");
        var text = GetString(msg, "message");
        var price = GetInt(msg, "price", 0);
        var color = GetInt(msg, "color", 0xFFD700);

        var displayText = $"[SC ¥{price}] {uname}: {text}";
        var fontSize = _config.SuperChat.FontSize;

        var item = new DanmakuItem
        {
            Text = displayText,
            Uname = uname,
            Color = color,
            FontSize = fontSize,
            Speed = _config.Danmaku.Speed * 0.3f,
            Opacity = _config.Danmaku.Opacity,
            IsSC = true,
            SCPrice = price,
            TextWidth = MeasureTextWidth?.Invoke(displayText, fontSize)
                       ?? displayText.Length * fontSize * 0.55f,
            MaxLifetimeMs = _config.SuperChat.DurationMs,
        };

        lock (_lock)
        {
            // Stack SC items vertically in the SC zone
            var scY = 4f;
            foreach (var existing in _items)
            {
                if (existing.IsSC)
                    scY = Math.Max(scY, existing.Y + existing.FontSize + 4);
            }
            // Clamp to SC zone
            item.Y = Math.Min(scY, ScZoneHeight - item.FontSize);
            item.X = ScreenWidth;
            _items.Add(item);
        }
    }

    public void Update()
    {
        var now = Environment.TickCount64;
        var dtMs = (int)(now - _lastFrameTicks);
        if (dtMs <= 0 || dtMs > 100) dtMs = 16;
        _lastFrameTicks = now;

        var dt = dtMs / 1000f;

        lock (_lock)
        {
            foreach (var item in _items)
            {
                if (item.IsSC)
                {
                    item.LifetimeMs += dtMs;
                    if (item.LifetimeMs > item.MaxLifetimeMs - 2000)
                    {
                        var fadeRatio = (item.MaxLifetimeMs - item.LifetimeMs) / 2000f;
                        item.Opacity = Math.Max(0, fadeRatio * _config.Danmaku.Opacity);
                    }
                }
                else
                {
                    item.X -= item.Speed * dt;
                    var fadeStart = ScreenWidth * 0.1f;
                    if (item.X + item.TextWidth < fadeStart)
                    {
                        var ratio = (item.X + item.TextWidth) / fadeStart;
                        item.Opacity = Math.Max(0, ratio * _config.Danmaku.Opacity);
                    }
                }
            }

            _items.RemoveAll(item => item.IsExpired(ScreenWidth));
            ActiveCount = _items.Count;
        }
    }

    public List<DanmakuItem> GetItems()
    {
        lock (_lock)
        {
            return new List<DanmakuItem>(_items);
        }
    }

    private void AssignTrack(DanmakuItem item)
    {
        var trackHeight = (ScreenHeight - ScZoneHeight) / _config.Danmaku.TrackCount;
        var track = _nextTrack % _config.Danmaku.TrackCount;
        _nextTrack++;

        var occupiedTracks = new HashSet<int>();
        foreach (var existing in _items)
        {
            // Only regular danmaku compete for tracks
            if (!existing.IsSC)
            {
                var t = (int)((existing.Y - ScZoneHeight) / trackHeight);
                if (t >= 0 && t < _config.Danmaku.TrackCount)
                    occupiedTracks.Add(t);
            }
        }

        var attempts = 0;
        while (occupiedTracks.Contains(track) && attempts < _config.Danmaku.TrackCount)
        {
            track = (track + 1) % _config.Danmaku.TrackCount;
            attempts++;
        }

        item.Track = track;
        item.Y = ScZoneHeight + track * trackHeight;
    }

    private static string GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var p) ? p.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string key, int def)
        => el.TryGetProperty(key, out var p) ? p.GetInt32() : def;

    private static long GetLong(JsonElement el, string key, long def)
        => el.TryGetProperty(key, out var p) ? p.GetInt64() : def;

    private static double GetDouble(JsonElement el, string key, double def)
        => el.TryGetProperty(key, out var p) ? p.GetDouble() : def;
}
