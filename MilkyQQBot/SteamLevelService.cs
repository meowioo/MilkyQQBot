using System.Text.Json;
using SkiaSharp;

namespace MilkyQQBot;

public class SteamLevelResult
{
    public string PersonaName { get; set; } = "未知玩家";
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
    public int Level { get; set; }
    public int CurrentXP { get; set; }
    public int XPNeededToLevelUp { get; set; }
    public int XPNeededCurrentLevel { get; set; }
    public bool IsPrivate { get; set; } = false;
}

public static class SteamLevelService
{
    private static readonly HttpClient _httpClient = new();
    private static string ApiKey => AppConfig.Current.Steam.ApiKey;

    static SteamLevelService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public static async Task<SteamLevelResult> GetLevelAsync(string steamId)
    {
        var result = new SteamLevelResult();

        try
        {
            // 1. 获取玩家名字和头像
            string summaryUrl = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={ApiKey}&steamids={steamId}";
            var summaryJson = await _httpClient.GetStringAsync(summaryUrl);
            using var summaryDoc = JsonDocument.Parse(summaryJson);
            
            var players = summaryDoc.RootElement.GetProperty("response").GetProperty("players");
            if (players.GetArrayLength() > 0)
            {
                var player = players[0];
                result.PersonaName = player.GetProperty("personaname").GetString() ?? "未知";
                result.AvatarUrl = player.TryGetProperty("avatarfull", out var avatar) ? avatar.GetString() ?? "" : "";
            }

            // 2. 获取玩家等级与经验值
            string badgeUrl = $"https://api.steampowered.com/IPlayerService/GetBadges/v1/?key={ApiKey}&steamid={steamId}";
            var badgeJson = await _httpClient.GetStringAsync(badgeUrl);
            using var badgeDoc = JsonDocument.Parse(badgeJson);
            
            var response = badgeDoc.RootElement.GetProperty("response");
            
            // 如果玩家隐藏了主页，接口可能返回空 response
            if (!response.TryGetProperty("player_level", out var levelElement))
            {
                result.IsPrivate = true;
            }
            else
            {
                result.Level = levelElement.GetInt32();
                result.CurrentXP = response.TryGetProperty("player_xp", out var xp) ? xp.GetInt32() : 0;
                result.XPNeededToLevelUp = response.TryGetProperty("player_xp_needed_to_level_up", out var xpTo) ? xpTo.GetInt32() : 0;
                result.XPNeededCurrentLevel = response.TryGetProperty("player_xp_needed_current_level", out var xpCur) ? xpCur.GetInt32() : 0;
            }

            // 下载头像
            if (!string.IsNullOrEmpty(result.AvatarUrl))
            {
                try
                {
                    var imgBytes = await _httpClient.GetByteArrayAsync(result.AvatarUrl);
                    result.Avatar = SKBitmap.Decode(imgBytes);
                }
                catch { }
            }
        }
        catch (HttpRequestException e) when (e.Message.Contains("401") || e.Message.Contains("403"))
        {
            result.IsPrivate = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[获取等级失败] {ex.Message}");
            return null;
        }

        return result;
    }

    // ==========================================
    // 动态渲染：新拟态手绘风 "赛博韭菜驾照"
    // ==========================================
    public static string GenerateLevelCardBase64(SteamLevelResult result)
    {
        int width = 720;
        int height = 360;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var bgColor = SKColor.Parse("#Fdfbf7");
        var strokeColor = SKColor.Parse("#2d3436");
        var shadowColor = SKColor.Parse("#e8e1d5");
        var levelColor = SKColor.Parse("#0984e3"); // 科技蓝
        var progressColor = SKColor.Parse("#00b894"); // 经验条绿色
        var redStampColor = SKColor.Parse("#ff7675"); // 印章红色

        canvas.Clear(bgColor);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        using var cardFillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };

        // 1. 画主卡片底座
        float cardX = 30, cardY = 30, cardW = width - 60, cardH = height - 60;
        canvas.DrawRoundRect(cardX + 8, cardY + 8, cardW, cardH, 15, 15, new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true });
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, 15, 15, cardFillPaint);
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, 15, 15, strokePaint);

        // 顶部小标题
        canvas.DrawText(" STEAM 赛博资产通行证 ", width / 2f, cardY + 35, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 22, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
        canvas.DrawLine(cardX, cardY + 50, cardX + cardW, cardY + 50, strokePaint);

        // 2. 左侧：绘制玩家头像
        var avatarRect = new SKRect(cardX + 30, cardY + 80, cardX + 180, cardY + 230);
        if (result.Avatar != null)
        {
            canvas.Save();
            var clipPath = new SKPath();
            clipPath.AddRoundRect(avatarRect, 12, 12);
            canvas.ClipPath(clipPath);
            canvas.DrawBitmap(result.Avatar, avatarRect);
            canvas.Restore();
        }
        canvas.DrawRoundRect(avatarRect, 12, 12, strokePaint);

        if (result.IsPrivate)
        {
            canvas.DrawText("玩家隐私受限", cardX + 220, cardY + 110, new SKPaint { Color = redStampColor, Typeface = typeface, TextSize = 32, IsAntialias = true, FakeBoldText = true });
            canvas.DrawText("该玩家隐藏了个人主页或资料...", cardX + 220, cardY + 160, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 20, IsAntialias = true });
        }
        else
        {
            // 3. 右侧：绘制基本信息
            float rightX = cardX + 220;
            
            // 名字
            string pName = result.PersonaName.Length > 12 ? result.PersonaName.Substring(0, 11) + "..." : result.PersonaName;
            canvas.DrawText(pName, rightX, cardY + 110, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 36, IsAntialias = true, FakeBoldText = true });

            // 等级大圆环
            float levelCircleX = rightX + 35;
            float levelCircleY = cardY + 165;
            canvas.DrawCircle(levelCircleX, levelCircleY, 30, new SKPaint { Color = levelColor, Style = SKPaintStyle.Fill, IsAntialias = true });
            canvas.DrawCircle(levelCircleX, levelCircleY, 30, strokePaint);
            
            // 动态调整等级文字的大小，防止百级大佬破框
            float lvlTextSize = result.Level > 99 ? 24 : 30;
            canvas.DrawText(result.Level.ToString(), levelCircleX, levelCircleY + (lvlTextSize/3), new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = lvlTextSize, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
            
            canvas.DrawText("Steam 等级", levelCircleX + 45, levelCircleY + 10, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });

            // 4. 计算并绘制经验进度条
            float barX = rightX;
            float barY = cardY + 220;
            float barW = 400;
            float barH = 24;

            // 进度条底框
            canvas.DrawRoundRect(barX, barY, barW, barH, 12, 12, new SKPaint { Color = SKColor.Parse("#dfe6e9"), Style = SKPaintStyle.Fill, IsAntialias = true });
            
            // 进度条填充计算 (安全防护除以0的情况)
            int xpInCurrentLevel = result.CurrentXP - result.XPNeededCurrentLevel;
            int totalXpForCurrentLevel = xpInCurrentLevel + result.XPNeededToLevelUp;
            float progressPercent = totalXpForCurrentLevel > 0 ? (float)xpInCurrentLevel / totalXpForCurrentLevel : 0;
            
            float fillW = barW * progressPercent;
            if (fillW > 0)
            {
                canvas.DrawRoundRect(barX, barY, fillW, barH, 12, 12, new SKPaint { Color = progressColor, Style = SKPaintStyle.Fill, IsAntialias = true });
            }
            // 进度条描边
            canvas.DrawRoundRect(barX, barY, barW, barH, 12, 12, strokePaint);

            // 经验值说明文字
            string xpText = $"总经验: {result.CurrentXP} XP   |   距离下级还差: {result.XPNeededToLevelUp} XP";
            canvas.DrawText(xpText, barX, barY + 50, new SKPaint { Color = SKColor.Parse("#636e72"), Typeface = typeface, TextSize = 18, IsAntialias = true, FakeBoldText = true });

            // 5. 绘制嘲讽称号印章 (右下角倾斜)
            string titleStamp = result.Level switch
            {
                < 10 => "十级萌新",
                < 30 => "初级韭菜",
                < 50 => "中级氪金者",
                < 100 => "高级慈善家",
                < 200 => "百级大佬",
                < 500 => "赛博巨星",
                _ => "G胖的干爹"
            };

            canvas.Save();
            canvas.Translate(rightX + 320, cardY + 110);
            canvas.RotateDegrees(15); // 让印章倾斜，更有手绘盖章的感觉
            var stampRect = new SKRect(-15, -35, 140, 15);
            canvas.DrawRoundRect(stampRect, 8, 8, new SKPaint { Color = redStampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true });
            canvas.DrawText(titleStamp, 60, -10, new SKPaint { Color = redStampColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
            canvas.Restore();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}