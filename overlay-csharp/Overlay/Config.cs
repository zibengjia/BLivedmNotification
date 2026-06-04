using System.Text.Json;

namespace Overlay;

public class Config
{
    public int RoomId { get; set; } = 510;
    public int DisplayIndex { get; set; } = 0;
    public string PipeName { get; set; } = "BlivedmOverlay";
    public DanmakuConfig Danmaku { get; set; } = new();
    public SuperChatConfig SuperChat { get; set; } = new();

    public class DanmakuConfig
    {
        public float FontSize { get; set; } = 28f;
        public float Speed { get; set; } = 300f;
        public float Opacity { get; set; } = 0.9f;
        public int TrackCount { get; set; } = 12;
    }

    public class SuperChatConfig
    {
        public float FontSize { get; set; } = 40f;
        public int DurationMs { get; set; } = 15000;
    }

    public static Config Load(string path = "config.json")
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
        }
        return new Config();
    }
}
