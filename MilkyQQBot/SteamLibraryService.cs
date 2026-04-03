using System.Text.Json;
using SkiaSharp;

namespace MilkyQQBot;

public class SteamLibraryGame
{
    public int AppId { get; set; }
    public string Name { get; set; } = "";
    public int PlaytimeForever { get; set; } 
    public DateTime? LastPlayed { get; set; } 
    public int AchievedCount { get; set; }
    public int TotalAchievements { get; set; }
    public bool HasAchievements { get; set; }
    public SKBitmap? Cover { get; set; } 
}

public class SteamLibraryResult
{
    public string PersonaName { get; set; } = "未知玩家";
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
    public int TotalGamesCount { get; set; }
    public List<SteamLibraryGame> TopGames { get; set; } = new();
    public bool IsPrivate { get; set; } = false;
}

public static class SteamLibraryService
{
    private static readonly HttpClient _httpClient = new();
    private static string ApiKey => AppConfig.Current.Steam.ApiKey;

    // 静态构造函数：全局伪装成正常的电脑浏览器，防止被 Steam 的防火墙拦截
    static SteamLibraryService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public static async Task<SteamLibraryResult> GetLibraryAsync(string steamId)
    {
        var result = new SteamLibraryResult();
        var downloadTasks = new List<Task>();

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
                if (!string.IsNullOrEmpty(result.AvatarUrl))
                {
                    downloadTasks.Add(Task.Run(async () => {
                        try { result.Avatar = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(result.AvatarUrl)); } catch { }
                    }));
                }
            }

            // 2. 获取拥有的游戏库
            string libraryUrl = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={ApiKey}&steamid={steamId}&format=json&include_appinfo=1";
            var libraryJson = await _httpClient.GetStringAsync(libraryUrl);
            using var libraryDoc = JsonDocument.Parse(libraryJson);
            
            var response = libraryDoc.RootElement.GetProperty("response");
            if (!response.TryGetProperty("games", out var gamesArray))
            {
                result.IsPrivate = true; // 没拿到 games 数组，说明游戏详情被设为私密了
                return result;
            }

            result.TotalGamesCount = response.TryGetProperty("game_count", out var gc) ? gc.GetInt32() : 0;

            var allGames = new List<SteamLibraryGame>();
            foreach (var game in gamesArray.EnumerateArray())
            {
                var libGame = new SteamLibraryGame
                {
                    AppId = game.GetProperty("appid").GetInt32(),
                    Name = game.GetProperty("name").GetString() ?? "未知游戏",
                    PlaytimeForever = game.TryGetProperty("playtime_forever", out var pt) ? pt.GetInt32() : 0
                };

                if (game.TryGetProperty("rtime_last_played", out var rtime) && rtime.GetInt64() > 0)
                {
                    libGame.LastPlayed = DateTimeOffset.FromUnixTimeSeconds(rtime.GetInt64()).LocalDateTime;
                }
                allGames.Add(libGame);
            }

            // 按总时长排序，取前 8 名最肝的游戏
            result.TopGames = allGames.OrderByDescending(g => g.PlaytimeForever).Take(8).ToList();

            // 3. 并发拉取这 8 款游戏的高清封面和成就数据！
            foreach (var g in result.TopGames)
            {
                // 拉取官方横幅封面
                string coverUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{g.AppId}/header.jpg";
                downloadTasks.Add(Task.Run(async () => {
                    try { g.Cover = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(coverUrl)); } catch { }
                }));

                // 拉取单款游戏成就 (换用极其安全的解析方式)
                downloadTasks.Add(Task.Run(async () => {
                    try
                    {
                        // 强制加上中文参数 l=schinese，确保接口返回规范数据
                        string achUrl = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?appid={g.AppId}&key={ApiKey}&steamid={steamId}&l=schinese";
                        
                        // 不再使用 GetStringAsync，改用 GetAsync 以便拦截 HTTP 错误代码
                        using var res = await _httpClient.GetAsync(achUrl);
                        
                        if (res.IsSuccessStatusCode)
                        {
                            var achJson = await res.Content.ReadAsStringAsync();
                            using var achDoc = JsonDocument.Parse(achJson);
                            
                            if (achDoc.RootElement.TryGetProperty("playerstats", out var stats))
                            {
                                // 安全解析 success，不管 Steam 抽风返回数字还是布尔都能扛住
                                bool isSuccess = false;
                                if (stats.TryGetProperty("success", out var succ))
                                {
                                    isSuccess = succ.ValueKind == JsonValueKind.True || 
                                                (succ.ValueKind == JsonValueKind.Number && succ.GetInt32() == 1) ||
                                                (succ.ValueKind == JsonValueKind.String && succ.GetString()?.ToLower() == "true");
                                }

                                if (isSuccess && stats.TryGetProperty("achievements", out var achArray))
                                {
                                    g.HasAchievements = true;
                                    g.TotalAchievements = achArray.GetArrayLength();
                                    
                                    foreach (var ach in achArray.EnumerateArray())
                                    {
                                        if (ach.TryGetProperty("achieved", out var achieved))
                                        {
                                            // 同理，安全解析 achieved 字段
                                            if ((achieved.ValueKind == JsonValueKind.Number && achieved.GetInt32() == 1) || 
                                                (achieved.ValueKind == JsonValueKind.True))
                                            {
                                                g.AchievedCount++;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // 留个后门：如果在控制台看到大量 403，说明目标玩家的主页是隐私状态
                            Console.WriteLine($"[成就拉取失败] HTTP {res.StatusCode} - 游戏 {g.Name} 可能无成就或被隐私拦截。");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[成就解析异常] 游戏 {g.Name} 发生报错: {ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(downloadTasks);
        }
        catch (HttpRequestException e) when (e.Message.Contains("401") || e.Message.Contains("403"))
        {
            result.IsPrivate = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[游戏库获取失败] {ex.Message}");
            return null;
        }

        return result;
    }

    // ==========================================
    // 动态渲染：新拟态手绘卡通风 游戏库卡片
    // ==========================================
    public static string GenerateLibraryCardBase64(SteamLibraryResult result)
    {
        int width = 850;
        int headerHeight = 160;
        int cardWidth = 770;
        int cardHeight = 130;
        int spacingY = 25;
        int height = headerHeight + (Math.Max(1, result.TopGames.Count) * (cardHeight + spacingY)) + 40;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // 新拟态卡通配色
        var bgColor = SKColor.Parse("#Fdfbf7");
        var strokeColor = SKColor.Parse("#2d3436");
        var shadowColor = SKColor.Parse("#e8e1d5");
        var timeColor = SKColor.Parse("#00b894");    
        var achColor = SKColor.Parse("#fdcb6e");     
        var grayTextColor = SKColor.Parse("#636e72");

        canvas.Clear(bgColor);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        using var cardFillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var shadowPaint = new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };

        // 1. 画顶部标题和头像
        var avatarRect = new SKRect(40, 40, 120, 120);
        if (result.Avatar != null)
        {
            canvas.Save();
            var clipPath = new SKPath();
            clipPath.AddRoundRect(avatarRect, 10, 10);
            canvas.ClipPath(clipPath);
            canvas.DrawBitmap(result.Avatar, avatarRect);
            canvas.Restore();
        }
        canvas.DrawRoundRect(avatarRect, 10, 10, strokePaint);

        canvas.DrawText($"{result.PersonaName} 的游戏资产底裤", 150, 80, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 36, IsAntialias = true, FakeBoldText = true });
        
        if (result.IsPrivate)
        {
            canvas.DrawText("🔒 该玩家隐藏了游戏详情，是个有秘密的人...", 150, 115, new SKPaint { Color = SKColor.Parse("#ff7675"), Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true });
            using var img = SKImage.FromBitmap(bitmap);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            return $"base64://{Convert.ToBase64String(d.ToArray())}";
        }

        canvas.DrawText($"库中总计吃灰游戏：{result.TotalGamesCount} 款 (按历史总时长排行)", 150, 115, new SKPaint { Color = grayTextColor, Typeface = typeface, TextSize = 20, IsAntialias = true });
        canvas.DrawLine(40, 145, width - 40, 145, strokePaint);

        // 2. 循环绘制游戏卡片
        float startY = 170;
        for (int i = 0; i < result.TopGames.Count; i++)
        {
            var game = result.TopGames[i];
            float y = startY + i * (cardHeight + spacingY);

            // 卡片底座
            canvas.DrawRoundRect(46, y + 6, cardWidth, cardHeight, 12, 12, shadowPaint);
            canvas.DrawRoundRect(40, y, cardWidth, cardHeight, 12, 12, cardFillPaint);
            canvas.DrawRoundRect(40, y, cardWidth, cardHeight, 12, 12, strokePaint);

            // 游戏横幅封面 
            float imgX = 55;
            float imgY = y + 18;
            float imgW = 200;
            float imgH = 93;
            var imgRect = new SKRect(imgX, imgY, imgX + imgW, imgY + imgH);
            
            if (game.Cover != null)
            {
                canvas.Save();
                var clipPath = new SKPath();
                clipPath.AddRoundRect(imgRect, 8, 8);
                canvas.ClipPath(clipPath);
                canvas.DrawBitmap(game.Cover, imgRect);
                canvas.Restore();
            }
            canvas.DrawRoundRect(imgRect, 8, 8, strokePaint);

            // 排名角标 
            canvas.DrawCircle(40, y, 18, cardFillPaint);
            canvas.DrawCircle(40, y, 18, strokePaint);
            canvas.DrawText((i + 1).ToString(), 33, y + 7, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true });

            // 游戏名称 
            float textX = imgX + imgW + 20;
            string gName = game.Name;
            var namePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true };
            if (namePaint.MeasureText(gName) > 280) 
            {
                while (namePaint.MeasureText(gName + "...") > 280 && gName.Length > 0)
                    gName = gName.Substring(0, gName.Length - 1);
                gName += "...";
            }
            canvas.DrawText(gName, textX, y + 45, namePaint);

            // 最后运行日期
            string lastPlayedStr = game.LastPlayed.HasValue ? $"最后运行: {game.LastPlayed.Value:yyyy-MM-dd}" : "从未运行过";
            canvas.DrawText(lastPlayedStr, textX, y + 90, new SKPaint { Color = grayTextColor, Typeface = typeface, TextSize = 18, IsAntialias = true });

            // ================= 右侧：时长与成就 =================
            float rightX = width - 60; 

            // 1. 总游戏时间 (小时)
            double hours = game.PlaytimeForever / 60.0;
            string timeStr = $"{hours:0.0} 小时";
            var timePaint = new SKPaint { Color = timeColor, Typeface = typeface, TextSize = 34, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Right };
            canvas.DrawText(timeStr, rightX, y + 55, timePaint);

            // 2. 成就徽章
            if (game.HasAchievements)
            {
                string achStr = $"成就: {game.AchievedCount} / {game.TotalAchievements}";
                var achTextPaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 18, IsAntialias = true, FakeBoldText = true };
                var achBounds = new SKRect();
                achTextPaint.MeasureText(achStr, ref achBounds);

                float badgeW = achBounds.Width + 20;
                float badgeH = 30;
                float badgeX = rightX - badgeW;
                float badgeY = y + 75;

                // 画黄色小圆角底框
                var badgeRect = new SKRect(badgeX, badgeY, badgeX + badgeW, badgeY + badgeH);
                canvas.DrawRoundRect(badgeRect, 6, 6, new SKPaint { Color = achColor, Style = SKPaintStyle.Fill, IsAntialias = true });
                canvas.DrawRoundRect(badgeRect, 6, 6, new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true });
                
                canvas.DrawText(achStr, badgeX + 10, badgeY + 21, achTextPaint);
            }
            else
            {
                canvas.DrawText("无成就系统", rightX, y + 95, new SKPaint { Color = grayTextColor, Typeface = typeface, TextSize = 18, IsAntialias = true, TextAlign = SKTextAlign.Right });
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}