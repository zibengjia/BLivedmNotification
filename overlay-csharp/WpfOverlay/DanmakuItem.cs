namespace Overlay;

public class DanmakuItem
{
    /// <summary>
    /// Display text (full string including uname for danmaku, or SC header for SC)
    /// </summary>
    public string Text { get; set; } = "";
    public string Uname { get; set; } = "";
    public int Color { get; set; } = 0xFFFFFF;
    public float X { get; set; }
    public float Y { get; set; }
    public float Speed { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public float FontSize { get; set; } = 28f;
    public float TextWidth { get; set; }
    public bool IsSC { get; set; }
    public int Track { get; set; }
    public long CreatedAt { get; set; }

    /// <summary>
    /// SC专用: 已存活时间 (ms)
    /// </summary>
    public long LifetimeMs { get; set; }

    /// <summary>
    /// SC专用: 最大存活时间 (ms)
    /// </summary>
    public int MaxLifetimeMs { get; set; } = 15000;

    /// <summary>
    /// SC专用: 价格 (元)
    /// </summary>
    public int SCPrice { get; set; }

    /// <summary>
    /// 鼠标悬停标志
    /// </summary>
    public bool IsHovered { get; set; }

    public bool IsExpired(float screenWidth)
    {
        if (IsSC)
        {
            return LifetimeMs >= MaxLifetimeMs;
        }
        return X + TextWidth < -50;
    }
}
