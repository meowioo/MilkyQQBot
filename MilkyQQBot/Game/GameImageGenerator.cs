using SkiaSharp;

namespace MilkyQQBot.Game;

public static class GameImageGenerator
{
    private static readonly HttpClient HttpClient = new();

    private static readonly string FontPath = Path.Combine(
        AppContext.BaseDirectory,
        "Game",
        "Assets",
        "汉仪正圆-75.ttf");

    private static readonly Lazy<SKTypeface> GameTypeface = new(() =>
    {
        if (!File.Exists(FontPath))
        {
            throw new FileNotFoundException($"找不到字体文件：{FontPath}");
        }

        var typeface = SKTypeface.FromFile(FontPath);
        if (typeface == null)
        {
            throw new InvalidOperationException($"字体加载失败：{FontPath}");
        }

        return typeface;
    });

    private static readonly object HelpCommandsLock = new();

    private static readonly List<(string Command, string Desc)> HelpCommands =
        new()
        {
            ("/game",  "开启 / 关闭本群冒险游戏（仅群主或管理员）"),
            ("/join",  "加入游戏，创建你的角色并从起点出发"),
            ("/go",    "行动一次，随机前进 0~4 步，终点会自动折返"),
            ("/look",  "查看当前地图，显示玩家在地图上的位置"),
            ("/info",  "查看自己的信息，或 @别人 查看对方信息"),
            ("/helpg", "查看这张游戏帮助菜单")
        };

    public static void RegisterHelpCommand(string command, string desc)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(desc))
        {
            return;
        }

        lock (HelpCommandsLock)
        {
            int index = HelpCommands.FindIndex(x =>
                string.Equals(x.Command, command, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                HelpCommands[index] = (command, desc);
            }
            else
            {
                HelpCommands.Add((command, desc));
            }
        }
    }

    public static void SetHelpCommands(IEnumerable<(string Command, string Desc)> commands)
    {
        if (commands is null)
        {
            return;
        }

        var normalized = commands
            .Where(x => !string.IsNullOrWhiteSpace(x.Command) && !string.IsNullOrWhiteSpace(x.Desc))
            .Select(x => (x.Command.Trim(), x.Desc.Trim()))
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        lock (HelpCommandsLock)
        {
            HelpCommands.Clear();
            HelpCommands.AddRange(normalized);
        }
    }

    public static IReadOnlyList<(string Command, string Desc)> GetHelpCommands()
    {
        lock (HelpCommandsLock)
        {
            return HelpCommands.ToList();
        }
    }

    public static async Task<string> GenerateMapAsync(IEnumerable<GamePlayer> players, string mapPath)
    {
        if (!File.Exists(mapPath))
        {
            throw new FileNotFoundException($"找不到地图文件：{mapPath}");
        }

        using var sourceMap = SKBitmap.Decode(mapPath)
                              ?? throw new InvalidOperationException("地图图片解码失败。请确认 QQMap.png 有效。");

        using var bitmap = new SKBitmap(sourceMap.Width, sourceMap.Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(sourceMap, 0, 0);

        using var titleBgPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 210),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var titleBorderPaint = new SKPaint
        {
            Color = SKColor.Parse("#2D3436"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };
        using var titlePaint = CreateTextPaint(24, SKColor.Parse("#2D3436"), true, SKTextAlign.Left);

        const string mapTitle = "启程原野";
        const float titleX = 16f;
        const float titleY = 16f;
        const float titleHeight = 44f;
        const float titlePaddingX = 14f;

        float titleWidth = titlePaint.MeasureText(mapTitle) + titlePaddingX * 2f;

        canvas.DrawRoundRect(titleX, titleY, titleWidth, titleHeight, 14, 14, titleBgPaint);
        canvas.DrawRoundRect(titleX, titleY, titleWidth, titleHeight, 14, 14, titleBorderPaint);

        titlePaint.GetFontMetrics(out var titleMetrics);
        float titleBaseline = titleY + titleHeight / 2f - (titleMetrics.Ascent + titleMetrics.Descent) / 2f;
        canvas.DrawText(mapTitle, titleX + titlePaddingX, titleBaseline, titlePaint);

        foreach (var player in players.OrderBy(p => p.JoinTime))
        {
            GameNode node = GameMapData.GetNode(player.Step);
            using var avatar = await LoadAvatarAsync(player.UserId, 32);
            DrawCircularAvatar(canvas, avatar, node.X - 16, node.Y - 16, 32);
        }

        return ToBase64Png(bitmap);
    }

    public static async Task<string> GeneratePlayerInfoAsync(GamePlayer player, string displayName)
    {
        const int width = 820;
        const int height = 500;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        SKColor bgColor = SKColor.Parse("#F7F2EA");
        SKColor cardColor = SKColors.White;
        SKColor strokeColor = SKColor.Parse("#2D3436");
        SKColor shadowColor = SKColor.Parse("#E4DBCE");
        SKColor accentColor = SKColor.Parse("#FFEAA7");
        SKColor subTextColor = SKColor.Parse("#636E72");

        canvas.Clear(bgColor);

        using var shadowPaint = new SKPaint
        {
            Color = shadowColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var cardPaint = new SKPaint
        {
            Color = cardColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var borderPaint = new SKPaint
        {
            Color = strokeColor,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3
        };
        using var accentPaint = new SKPaint
        {
            Color = accentColor,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        canvas.DrawRoundRect(34, 34, width - 68, height - 82, 28, 28, shadowPaint);
        canvas.DrawRoundRect(28, 28, width - 68, height - 82, 28, 28, cardPaint);
        canvas.DrawRoundRect(28, 28, width - 68, height - 82, 28, 28, borderPaint);

        canvas.DrawRoundRect(58, 58, width - 116, 76, 20, 20, accentPaint);
        canvas.DrawRoundRect(58, 58, width - 116, 76, 20, 20, borderPaint);

        using var titlePaint = CreateTextPaint(34, strokeColor, true, SKTextAlign.Left);
        using var namePaint = CreateTextPaint(30, strokeColor, true, SKTextAlign.Left);
        using var subPaint = CreateTextPaint(18, subTextColor, false, SKTextAlign.Left);
        using var labelPaint = CreateTextPaint(20, strokeColor, true, SKTextAlign.Left);
        using var valuePaint = CreateTextPaint(24, strokeColor, true, SKTextAlign.Left);
        using var chipPaint = new SKPaint
        {
            Color = SKColor.Parse("#FFF7E6"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        canvas.DrawText("冒险者档案", 82, 108, titlePaint);

        using var avatar = await LoadAvatarAsync(player.UserId, 120);
        DrawCircularAvatar(canvas, avatar, 84, 162, 120);

        string statusText = player.HP <= 0 ? "【重伤】" : "【正常】";
        canvas.DrawText(displayName, 236, 228, namePaint);
        canvas.DrawText($"状态 {statusText}", 236, 264, subPaint);
        canvas.DrawText($"QQ {player.UserId}", 236, 294, subPaint);
        canvas.DrawText($"当前步数  第 {player.Step} 步", 236, 324, subPaint);

        DrawStatChip(canvas, 84, 368, 150, 72, "生命", $"{player.HP}/{player.MaxHP}", chipPaint, borderPaint, labelPaint, valuePaint);
        DrawStatChip(canvas, 252, 368, 120, 72, "攻击", player.ATK.ToString(), chipPaint, borderPaint, labelPaint, valuePaint);
        DrawStatChip(canvas, 390, 368, 120, 72, "防御", player.DEF.ToString(), chipPaint, borderPaint, labelPaint, valuePaint);
        DrawStatChip(canvas, 528, 368, 120, 72, "金币", player.Gold.ToString(), chipPaint, borderPaint, labelPaint, valuePaint);
        DrawStatChip(canvas, 666, 368, 100, 72, "步数", player.Step.ToString(), chipPaint, borderPaint, labelPaint, valuePaint);

        using var progressBg = new SKPaint
        {
            Color = SKColor.Parse("#ECECEC"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var progressValue = new SKPaint
        {
            Color = SKColor.Parse("#74B9FF"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        float barX = 528;
        float barY = 182;
        float barW = 224;
        float barH = 18;
        float hpRatio = player.MaxHP <= 0 ? 0 : Math.Clamp(player.HP / (float)player.MaxHP, 0f, 1f);

        canvas.DrawText("生命条", 528, 170, labelPaint);
        canvas.DrawRoundRect(barX, barY, barW, barH, 9, 9, progressBg);
        canvas.DrawRoundRect(barX, barY, barW * hpRatio, barH, 9, 9, progressValue);
        canvas.DrawRoundRect(barX, barY, barW, barH, 9, 9, borderPaint);

        canvas.DrawText("基础属性", 528, 248, labelPaint);
        canvas.DrawText("初始生命 100/100", 528, 280, subPaint);
        canvas.DrawText("基础攻击 10", 528, 308, subPaint);
        canvas.DrawText("基础防御 5", 528, 336, subPaint);

        return ToBase64Png(bitmap);
    }

    public static Task<string> GenerateHelpMenuAsync()
    {
        List<(string Command, string Desc)> commands;
        lock (HelpCommandsLock)
        {
            commands = HelpCommands.ToList();
        }

        return GenerateHelpMenuAsync(commands, "By 奈奈川");
    }

    public static Task<string> GenerateHelpMenuAsync(
        IEnumerable<(string Command, string Desc)> commandItems,
        string badgeText = "By 奈奈川")
    {
        var commands = commandItems?
                           .Where(x => !string.IsNullOrWhiteSpace(x.Command) && !string.IsNullOrWhiteSpace(x.Desc))
                           .Select(x => (Command: x.Command.Trim(), Desc: x.Desc.Trim()))
                           .ToList()
                       ?? new List<(string Command, string Desc)>();

        if (commands.Count == 0)
        {
            lock (HelpCommandsLock)
            {
                commands = HelpCommands.ToList();
            }
        }

        const int width = 1080;
        const int padding = 48;
        const int headerHeight = 220;
        const int rowGap = 22;
        const float descLineHeight = 36f;
        const float tipsHeight = 190f;

        using var descMeasurePaint = CreateTextPaint(28, SKColor.Parse("#445067"), false, SKTextAlign.Left);

        var rows = new List<HelpRowLayout>();
        foreach (var item in commands)
        {
            var lines = WrapText(item.Desc, descMeasurePaint, width - padding * 2 - 330);
            if (lines.Count == 0)
            {
                lines.Add(string.Empty);
            }

            float descBlockHeight = Math.Max(descLineHeight, lines.Count * descLineHeight);
            float contentHeight = Math.Max(60f, descBlockHeight);
            float cardHeight = Math.Max(104f, contentHeight + 36f);

            rows.Add(new HelpRowLayout(item.Command, lines, cardHeight));
        }

        float rowsTotalHeight = rows.Sum(r => r.CardHeight) + Math.Max(0, rows.Count - 1) * rowGap;
        int height = (int)Math.Ceiling(headerHeight + 20 + rowsTotalHeight + 20 + tipsHeight + 80);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#F7FAFF"));

        DrawBackground(canvas, width, height);
        DrawHeader(canvas, width, padding, badgeText);

        float currentY = headerHeight + 20;

        for (int i = 0; i < rows.Count; i++)
        {
            DrawCommandCard(
                canvas,
                x: padding,
                y: currentY,
                width: width - padding * 2,
                height: rows[i].CardHeight,
                command: rows[i].Command,
                descLines: rows[i].DescLines,
                accentColor: GetAccentColor(i));

            currentY += rows[i].CardHeight + rowGap;
        }

        DrawTipsCard(canvas, padding, currentY, width - padding * 2);
        DrawFooter(canvas, width, height);

        return Task.FromResult(ToBase64Png(bitmap));
    }

    private static void DrawStatChip(
        SKCanvas canvas,
        float x,
        float y,
        float w,
        float h,
        string label,
        string value,
        SKPaint fillPaint,
        SKPaint borderPaint,
        SKPaint labelPaint,
        SKPaint valuePaint)
    {
        canvas.DrawRoundRect(x, y, w, h, 18, 18, fillPaint);
        canvas.DrawRoundRect(x, y, w, h, 18, 18, borderPaint);

        canvas.DrawText(label, x + 14, y + 26, labelPaint);
        canvas.DrawText(value, x + 14, y + 56, valuePaint);
    }

    private static SKPaint CreateTextPaint(float size, SKColor color, bool bold, SKTextAlign align)
    {
        return new SKPaint
        {
            Typeface = GameTypeface.Value,
            TextSize = size,
            Color = color,
            IsAntialias = true,
            FakeBoldText = bold,
            TextAlign = align
        };
    }

    private static void DrawCircularAvatar(SKCanvas canvas, SKBitmap? avatar, float x, float y, float size)
    {
        float radius = size / 2f;
        float centerX = x + radius;
        float centerY = y + radius;

        using var fallbackPaint = new SKPaint
        {
            Color = SKColor.Parse("#D6EAF8"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        using var borderPaint = new SKPaint
        {
            Color = SKColor.Parse("#2D3436"),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };

        canvas.DrawCircle(centerX, centerY, radius, fallbackPaint);

        if (avatar is not null)
        {
            canvas.Save();
            using var clipPath = new SKPath();
            clipPath.AddCircle(centerX, centerY, radius);
            canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);
            canvas.DrawBitmap(avatar, x, y);
            canvas.Restore();
        }

        canvas.DrawCircle(centerX, centerY, radius, borderPaint);
    }

    private static async Task<SKBitmap?> LoadAvatarAsync(long qqId, int size)
    {
        try
        {
            byte[] bytes = await HttpClient.GetByteArrayAsync($"http://q1.qlogo.cn/g?b=qq&nk={qqId}&s=100");
            using var original = SKBitmap.Decode(bytes);
            if (original is null)
            {
                return null;
            }

            if (original.Width == size && original.Height == size)
            {
                return original.Copy();
            }

            return original.Resize(new SKImageInfo(size, size), SKFilterQuality.High) ?? original.Copy();
        }
        catch
        {
            return null;
        }
    }

    private static string ToBase64Png(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }

    private static void DrawBackground(SKCanvas canvas, int width, int height)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                new[]
                {
                    SKColor.Parse("#F7FAFF"),
                    SKColor.Parse("#EEF7FF"),
                    SKColor.Parse("#F9F5FF")
                },
                null,
                SKShaderTileMode.Clamp)
        };

        canvas.DrawRect(new SKRect(0, 0, width, height), paint);

        using var decoPaint1 = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#DDF3FF").WithAlpha(130)
        };
        canvas.DrawCircle(120, 110, 72, decoPaint1);
        canvas.DrawCircle(width - 120, 160, 56, decoPaint1);

        using var decoPaint2 = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#F5DFFF").WithAlpha(110)
        };
        canvas.DrawCircle(width - 180, height - 120, 86, decoPaint2);
        canvas.DrawCircle(150, height - 80, 52, decoPaint2);
    }

    private static void DrawHeader(SKCanvas canvas, int width, int padding, string badgeText)
    {
        var cardRect = new SKRect(padding, 36, width - padding, 190);

        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(80, 110, 180, 22)
        };
        using var cardPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };

        canvas.DrawRoundRect(new SKRect(cardRect.Left + 6, cardRect.Top + 8, cardRect.Right + 6, cardRect.Bottom + 8), 34, 34, shadowPaint);
        canvas.DrawRoundRect(cardRect, 34, 34, cardPaint);

        using var titlePaint = CreateTextPaint(54, SKColor.Parse("#24324A"), true, SKTextAlign.Left);
        using var subPaint = CreateTextPaint(28, SKColor.Parse("#5E6B84"), false, SKTextAlign.Left);

        canvas.DrawText("启程原野 · 游戏帮助", padding + 34, 96, titlePaint);
        canvas.DrawText("游戏指令说明", padding + 36, 138, subPaint);

        using var badgePaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#E8F6EC")
        };
        using var badgeTextPaint = CreateTextPaint(24, SKColor.Parse("#2D8A57"), true, SKTextAlign.Center);

        float badgePaddingX = 26f;
        float badgeHeight = 52f;
        float badgeWidth = Math.Max(160f, badgeTextPaint.MeasureText(badgeText) + badgePaddingX * 2);
        float badgeRight = width - padding - 22;
        float badgeLeft = badgeRight - badgeWidth;
        var badgeRect = new SKRect(badgeLeft, 70, badgeRight, 70 + badgeHeight);

        canvas.DrawRoundRect(badgeRect, 22, 22, badgePaint);
        DrawCenteredText(canvas, badgeText, badgeTextPaint, badgeRect);
    }

    private static void DrawCommandCard(
        SKCanvas canvas,
        float x,
        float y,
        float width,
        float height,
        string command,
        IReadOnlyList<string> descLines,
        SKColor accentColor)
    {
        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(76, 102, 150, 18)
        };
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White
        };
        using var leftBarPaint = new SKPaint
        {
            IsAntialias = true,
            Color = accentColor
        };
        using var cmdBgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = accentColor.WithAlpha(35)
        };

        canvas.DrawRoundRect(new SKRect(x + 4, y + 6, x + width + 4, y + height + 6), 28, 28, shadowPaint);
        canvas.DrawRoundRect(new SKRect(x, y, x + width, y + height), 28, 28, bgPaint);
        canvas.DrawRoundRect(new SKRect(x + 18, y + 18, x + 34, y + height - 18), 8, 8, leftBarPaint);

        float cmdChipWidth = 206f;
        float cmdChipHeight = 60f;
        float cmdChipX = x + 56;
        float cmdChipY = y + (height - cmdChipHeight) / 2f;
        var chipRect = new SKRect(cmdChipX, cmdChipY, cmdChipX + cmdChipWidth, cmdChipY + cmdChipHeight);
        canvas.DrawRoundRect(chipRect, 22, 22, cmdBgPaint);

        using var cmdPaint = CreateTextPaint(34, accentColor, true, SKTextAlign.Center);
        DrawCenteredText(canvas, command, cmdPaint, chipRect);

        using var descPaint = CreateTextPaint(28, SKColor.Parse("#445067"), false, SKTextAlign.Left);
        const float descLineHeight = 36f;
        float descBlockHeight = Math.Max(descLineHeight, descLines.Count * descLineHeight);
        float descX = x + 290;
        float descTop = y + (height - descBlockHeight) / 2f;

        DrawWrappedTextLines(canvas, descLines, descPaint, descX, descTop, descLineHeight);
    }

    private static void DrawTipsCard(SKCanvas canvas, float x, float y, float width)
    {
        using var bgPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColor.Parse("#FFFDF8")
        };

        canvas.DrawRoundRect(new SKRect(x, y, x + width, y + 190), 28, 28, bgPaint);

        using var titlePaint = CreateTextPaint(32, SKColor.Parse("#8B5E00"), true, SKTextAlign.Left);
        using var textPaint = CreateTextPaint(26, SKColor.Parse("#6A5B3F"), false, SKTextAlign.Left);

        canvas.DrawText("游玩提示", x + 28, y + 48, titlePaint);
        canvas.DrawText("• 游戏指令仅限群聊中使用", x + 32, y + 92, textPaint);
        canvas.DrawText("• 第一次游玩请先发送 /join", x + 32, y + 126, textPaint);
        canvas.DrawText("• 玩家走到终点后会自动掉头往回走", x + 32, y + 160, textPaint);
    }

    private static void DrawFooter(SKCanvas canvas, int width, int height)
    {
        using var footerPaint = CreateTextPaint(22, SKColor.Parse("#7B879A"), false, SKTextAlign.Left);
        string footer = "发送 /helpg 可再次查看本菜单";
        float footerWidth = footerPaint.MeasureText(footer);
        canvas.DrawText(footer, (width - footerWidth) / 2f, height - 36, footerPaint);
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, SKPaint paint, SKRect rect)
    {
        paint.GetFontMetrics(out var metrics);
        float baseline = rect.MidY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, rect.MidX, baseline, paint);
    }

    private static void DrawWrappedTextLines(
        SKCanvas canvas,
        IReadOnlyList<string> lines,
        SKPaint paint,
        float x,
        float topY,
        float lineHeight)
    {
        paint.GetFontMetrics(out var metrics);
        float baseline = topY - metrics.Ascent;

        for (int i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], x, baseline + i * lineHeight, paint);
        }
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add(string.Empty);
            return lines;
        }

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add(string.Empty);
                continue;
            }

            string current = string.Empty;

            foreach (char c in paragraph)
            {
                string test = current + c;
                if (paint.MeasureText(test) <= maxWidth)
                {
                    current = test;
                }
                else
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        lines.Add(current);
                    }

                    current = c.ToString();
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                lines.Add(current);
            }
        }

        return lines;
    }

    private static SKColor GetAccentColor(int index)
    {
        SKColor[] colors =
        {
            SKColor.Parse("#5B8DEF"),
            SKColor.Parse("#55B97A"),
            SKColor.Parse("#F08B5B"),
            SKColor.Parse("#A66BFF"),
            SKColor.Parse("#E05C87"),
            SKColor.Parse("#3AA7A3")
        };

        return colors[index % colors.Length];
    }

    private sealed class HelpRowLayout
    {
        public HelpRowLayout(string command, List<string> descLines, float cardHeight)
        {
            Command = command;
            DescLines = descLines;
            CardHeight = cardHeight;
        }

        public string Command { get; }
        public List<string> DescLines { get; }
        public float CardHeight { get; }
    }
}