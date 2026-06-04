namespace Overlay.Services;

/// <summary>
/// Manages Config loading, saving, and UI synchronization.
/// </summary>
public class ConfigService
{
    private Config _config;
    private string _configPath;

    public Config Config => _config;
    public string ConfigPath => _configPath;

    public ConfigService(string? path = null)
    {
        _configPath = Config.ResolveConfigPath(path ?? "config.json");
        Reload();
    }

    public void Reload()
    {
        _config = Config.Load(_configPath);
        _configPath = _config.ConfigPath ?? _configPath;
    }

    public void Save()
    {
        _config.Save(_configPath);
        Reload(); // sync internal state
    }

    /// <summary>
    /// Write values from page controls into config object.
    /// </summary>
    public void ApplyBasicSettings(int roomId, string pipeName, string sessdata, int displayIndex)
    {
        _config.RoomId = roomId;
        _config.PipeName = pipeName;
        _config.Sessdata = sessdata;
        _config.DisplayIndex = displayIndex;
    }

    public void ApplyDanmakuSettings(float fontSize, float speed, float opacity, int trackCount)
    {
        _config.Danmaku.FontSize = fontSize;
        _config.Danmaku.Speed = speed;
        _config.Danmaku.Opacity = opacity;
        _config.Danmaku.TrackCount = trackCount;
    }

    public void ApplySuperChatSettings(float fontSize, int durationMs)
    {
        _config.SuperChat.FontSize = fontSize;
        _config.SuperChat.DurationMs = durationMs;
    }
}
