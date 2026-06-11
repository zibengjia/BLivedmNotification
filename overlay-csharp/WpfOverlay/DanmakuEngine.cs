using System.Text.Json;

namespace Overlay;

public class DanmakuEngine
{
    private readonly List<DanmakuItem> _items = new();
    private readonly object _lock = new();
    private readonly Config _config;
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
    /// Set by OverlayWindow after renderer initialization.
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

        // Read B站 API-provided SC colors and duration
        var bgColor = GetString(msg, "background_color");
        var bgBottomColor = GetString(msg, "background_bottom_color");
        var bgPriceColor = GetString(msg, "background_price_color");
        var apiTimeSec = GetInt(msg, "time", 0);

        var displayText = $"[SC ¥{price}] {uname}: {text}";
        var fontSize = _config.SuperChat.FontSize;

        // Use API-provided duration if available; otherwise fall back to config
        var durationMs = apiTimeSec > 0 ? apiTimeSec * 1000 : _config.SuperChat.DurationMs;

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
            SCBackgroundColor = bgColor,
            SCBackgroundBottomColor = bgBottomColor,
            SCBackgroundPriceColor = bgPriceColor,
            TextWidth = MeasureTextWidth?.Invoke(displayText, fontSize)
                       ?? displayText.Length * fontSize * 0.55f,
            MaxLifetimeMs = durationMs,
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

            // Position X based on alignment config
            item.X = CalcScX(item.TextWidth);
            _items.Add(item);
        }
    }

    /// <summary>
    /// Calculate SC X position based on alignment setting.
    /// </summary>
    private float CalcScX(float textWidth)
    {
        const float margin = 4f;
        return _config.SuperChat.Alignment switch
        {
            "Center" => Math.Max(margin, (ScreenWidth - textWidth) / 2f),
            "Right"  => Math.Max(margin, ScreenWidth - textWidth - margin),
            _        => margin, // "Left"
        };
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
        var maxTracks = Math.Max(1, _config.Danmaku.TrackCount);
        var trackHeight = (ScreenHeight - ScZoneHeight) / (float)maxTracks;
        var entryWidth = item.TextWidth;
        var spacingMul = GetDensityMultiplier();

        // First pass: find first track with enough free space
        foreach (int t in GetTrackScanOrder(maxTracks))
        {
            var freePx = GetTrackFreeSpace(t, trackHeight);
            if (freePx >= entryWidth * spacingMul)
            {
                item.Track = t;
                item.Y = ScZoneHeight + t * trackHeight;
                return;
            }
        }

        // Fallback: all tracks blocked — pick the one with most free space
        var bestTrack = 0;
        var bestFree = float.MinValue;
        foreach (int t in GetTrackScanOrder(maxTracks))
        {
            var freePx = GetTrackFreeSpace(t, trackHeight);
            if (freePx > bestFree)
            {
                bestFree = freePx;
                bestTrack = t;
            }
        }

        item.Track = bestTrack;
        item.Y = ScZoneHeight + bestTrack * trackHeight;
    }

    /// <summary>
    /// Get free horizontal space on a given track (rightmost occupied pixel to screen edge).
    /// </summary>
    private float GetTrackFreeSpace(int trackIndex, float trackHeight)
    {
        var trackTop = ScZoneHeight + trackIndex * trackHeight;
        var trackBottom = trackTop + trackHeight;
        var rightmost = 0f;
        foreach (var existing in _items)
        {
            if (existing.IsSC) continue;
            if (existing.Y >= trackTop && existing.Y < trackBottom)
            {
                var rightEdge = existing.X + existing.TextWidth;
                if (rightEdge > rightmost)
                    rightmost = rightEdge;
            }
        }
        return ScreenWidth - rightmost;
    }

    /// <summary>
    /// Returns track indices in scan order based on position priority config.
    /// </summary>
    private IEnumerable<int> GetTrackScanOrder(int maxTracks)
    {
        var priority = _config.Danmaku.PositionPriority;

        switch (priority)
        {
            case "Bottom":
                for (int t = maxTracks - 1; t >= 0; t--)
                    yield return t;
                break;

            case "Center":
                // Start from middle, alternate outward
                int mid = maxTracks / 2;
                yield return mid;
                for (int offset = 1; offset <= mid; offset++)
                {
                    if (mid - offset >= 0)
                        yield return mid - offset;
                    if (mid + offset < maxTracks)
                        yield return mid + offset;
                }
                // If even count and we missed the last one
                if (maxTracks % 2 == 0 && mid + 1 < maxTracks)
                    yield return mid + 1;
                break;

            default: // "Top" and any unknown value
                for (int t = 0; t < maxTracks; t++)
                    yield return t;
                break;
        }
    }

    /// <summary>
    /// Returns spacing multiplier based on density setting.
    /// Higher = more free space needed = sparser danmaku.
    /// </summary>
    private float GetDensityMultiplier()
    {
        return _config.Danmaku.Density switch
        {
            "Low" => 2.5f,
            "High" => 0.7f,
            _ => 1.2f, // "Medium" and any unknown value
        };
    }

    /// <summary>
    /// Check if mouse position hovers over any danmaku.
    /// Sets IsHovered flag so renderer can hide hovered items.
    /// </summary>
    public void CheckHover(float mouseX, float mouseY)
    {
        if (!_config.Danmaku.HoverHideEnabled)
        {
            lock (_lock)
                foreach (var item in _items)
                    item.IsHovered = false;
            return;
        }

        lock (_lock)
        {
            foreach (var item in _items)
            {
                item.IsHovered = mouseX >= item.X
                              && mouseX <= item.X + item.TextWidth
                              && mouseY >= item.Y
                              && mouseY <= item.Y + item.FontSize;
            }
        }
    }

    private static string GetString(JsonElement el, string key)
        => el.TryGetProperty(key, out var p) ? p.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string key, int def)
        => el.TryGetProperty(key, out var p) ? p.GetInt32() : def;

    private static long GetLong(JsonElement el, string key, long def)
        => el.TryGetProperty(key, out var p) ? p.GetInt64() : def;

}
