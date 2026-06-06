using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overlay;

public class Config
{
    [JsonPropertyName("room_id")]
    public int RoomId { get; set; } = 510;

    [JsonPropertyName("display_index")]
    public int DisplayIndex { get; set; } = 0;

    [JsonPropertyName("pipe_name")]
    public string PipeName { get; set; } = "BlivedmOverlay";

    [JsonPropertyName("sessdata")]
    public string Sessdata { get; set; } = "";

    [JsonPropertyName("python_path")]
    public string PythonPath { get; set; } = "";

    [JsonPropertyName("danmaku")]
    public DanmakuConfig Danmaku { get; set; } = new();

    [JsonPropertyName("super_chat")]
    public SuperChatConfig SuperChat { get; set; } = new();

    [JsonPropertyName("rooms")]
    public List<RoomEntry> Rooms { get; set; } = new();

    [JsonPropertyName("selected_room_index")]
    public int SelectedRoomIndex { get; set; } = 0;

    /// <summary>Path this config was loaded from (set by Load).</summary>
    [JsonIgnore]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Get the currently selected room entry, or create a default one.
    /// </summary>
    [JsonIgnore]
    public RoomEntry CurrentRoom
    {
        get
        {
            if (Rooms.Count > 0 && SelectedRoomIndex >= 0 && SelectedRoomIndex < Rooms.Count)
                return Rooms[SelectedRoomIndex];
            // Fall back to the legacy room_id
            var entry = new RoomEntry { RoomId = RoomId };
            Rooms.Add(entry);
            SelectedRoomIndex = 0;
            return entry;
        }
    }

    public class RoomEntry
    {
        [JsonPropertyName("room_id")]
        public int RoomId { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        public string DisplayText => string.IsNullOrEmpty(Label) ? $"{RoomId}" : $"{Label} ({RoomId})";
    }

    public class DanmakuConfig
    {
        [JsonPropertyName("font_family")]
        public string FontFamily { get; set; } = "Microsoft YaHei UI";

        [JsonPropertyName("font_size")]
        public float FontSize { get; set; } = 28f;

        [JsonPropertyName("speed")]
        public float Speed { get; set; } = 300f;

        [JsonPropertyName("opacity")]
        public float Opacity { get; set; } = 0.9f;

        [JsonPropertyName("track_count")]
        public int TrackCount { get; set; } = 12;

        [JsonPropertyName("font_weight")]
        public string FontWeight { get; set; } = "Normal";

        [JsonPropertyName("shadow_enabled")]
        public bool ShadowEnabled { get; set; } = true;

        [JsonPropertyName("shadow_opacity")]
        public float ShadowOpacity { get; set; } = 0.6f;

        [JsonPropertyName("shadow_offset")]
        public float ShadowOffset { get; set; } = 2.0f;

        [JsonPropertyName("position_priority")]
        public string PositionPriority { get; set; } = "Top";

        [JsonPropertyName("density")]
        public string Density { get; set; } = "Medium";

        [JsonPropertyName("hover_hide_enabled")]
        public bool HoverHideEnabled { get; set; } = false;
    }

    public class SuperChatConfig
    {
        [JsonPropertyName("font_size")]
        public float FontSize { get; set; } = 40f;

        [JsonPropertyName("duration_ms")]
        public int DurationMs { get; set; } = 15000;
    }

    /// <summary>Save to the path this config was loaded from (or a given path).</summary>
    public void Save(string? path = null)
    {
        path ??= ConfigPath ?? "config.json";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Resolve config.json by searching from exe dir upward.
    /// Falls back to the given path if not found in parent chain.
    /// </summary>
    public static string ResolveConfigPath(string path = "config.json")
    {
        if (Path.IsPathRooted(path) && File.Exists(path))
            return Path.GetFullPath(path);
        if (File.Exists(path))
            return Path.GetFullPath(path);

        // Walk up from the executable directory
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, Path.GetFileName(path));
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }

        return Path.GetFullPath(path);
    }

    /// <summary>Load config from file, searching upward from exe dir.</summary>
    public static Config Load(string? path = null)
    {
        path ??= "config.json";
        var resolved = ResolveConfigPath(path);
        try
        {
            if (File.Exists(resolved))
            {
                var json = File.ReadAllText(resolved);
                var config = JsonSerializer.Deserialize<Config>(json);
                if (config != null)
                {
                    config.ConfigPath = resolved;

                    // Migrate: if rooms list is empty, populate from legacy room_id
                    if (config.Rooms.Count == 0 && config.RoomId > 0)
                    {
                        config.Rooms.Add(new RoomEntry { RoomId = config.RoomId, Label = "" });
                        config.SelectedRoomIndex = 0;
                    }

                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
        }

        var fallback = new Config { ConfigPath = resolved };
        return fallback;
    }
}
