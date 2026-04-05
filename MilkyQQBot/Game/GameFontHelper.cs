using SkiaSharp;

namespace MilkyQQBot.Game;

public static class GameFontHelper
{
    private static readonly Lazy<SKTypeface> ZhTypeface = new(() =>
    {
        string fontPath = Path.Combine(
            AppContext.BaseDirectory,
            "Game",
            "Assets",
            "Fonts",
            "NotoSansSC-Regular.otf");

        if (!File.Exists(fontPath))
        {
            throw new FileNotFoundException($"找不到中文字体文件：{fontPath}");
        }

        var typeface = SKTypeface.FromFile(fontPath);
        if (typeface == null)
        {
            throw new Exception($"字体加载失败：{fontPath}");
        }

        return typeface;
    });

    public static SKPaint CreatePaint(float textSize, string color, bool bold = false)
    {
        return new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse(color),
            TextSize = textSize,
            Typeface = ZhTypeface.Value,
            FakeBoldText = bold
        };
    }
}