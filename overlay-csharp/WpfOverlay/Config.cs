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

    /// <summary>Path this config was loaded from (set by Load).</summary>
    [JsonIgnore]
    public string? ConfigPath { get; set; }

    public class DanmakuConfig
    {
        [JsonPropertyName("font_size")]
        public float FontSize { get; set; } = 28f;

        [JsonPropertyName("speed")]
        public float Speed { get; set; } = 300f;

        [JsonPropertyName("opacity")]
        public float Opacity { get; set; } = 0.9f;

        [JsonPropertyName("track_count")]
        public int TrackCount { get; set; } = 12;
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
