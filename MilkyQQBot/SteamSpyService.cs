using System.Text.Json;
using SkiaSharp;

namespace MilkyQQBot;

// ==========================================
// 数据实体类
// ==========================================
public class SteamSpyResult
{
    public string SteamId { get; set; } = "";
    public string PersonaName { get; set; } = "未知玩家";
    public int PersonaState { get; set; }
    public string PlayingGame { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
    public List<RecentGame> RecentGames { get; set; } = new();
    public List<SteamFriendInfo> TopFriends { get; set; } = new(); // 活跃好友雷达
    public bool IsFriendListPrivate { get; set; } = false; // 标记好友列表是否隐藏
}

public class RecentGame
{
    public string Name { get; set; } = "";
    public int Playtime2Weeks { get; set; } // 分钟
}

public class SteamFriendInfo
{
    public string PersonaName { get; set; } = "未知好友";
    public int PersonaState { get; set; } 
    public DateTime LastLogoff { get; set; } 
    public string PlayingGame { get; set; } = ""; 
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
}

public static class SteamSpyService
{
    private static readonly HttpClient _httpClient = new();
    private static string ApiKey => AppConfig.Current.Steam.ApiKey;

    // ==========================================
    // 自动解析 Steam 自定义短链接
    // ==========================================
    public static async Task<string> ResolveVanityUrlAsync(string vanityName)
    {
        try
        {
            string url = $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/?key={ApiKey}&vanityurl={vanityName}";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            
            var response = doc.RootElement.GetProperty("response");
            if (response.TryGetProperty("success", out var success) && success.GetInt32() == 1)
            {
                return response.GetProperty("steamid").GetString() ?? "";
            }
        }
        catch { }
        return null;
    }

    // ==========================================
    // 终极聚合查询：目标信息 + 最近游戏 + 活跃好友
    // ==========================================
    public static async Task<SteamSpyResult> GetFullSpyReportAsync(string steamId)
    {
        var result = new SteamSpyResult { SteamId = steamId };
        var avatarDownloadTasks = new List<Task>(); // 用于并发下载头像

        try
        {
            // 1. 获取目标玩家基本信息
            string summaryUrl = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={ApiKey}&steamids={steamId}";
            var summaryJson = await _httpClient.GetStringAsync(summaryUrl);
            using var summaryDoc = JsonDocument.Parse(summaryJson);
            
            var players = summaryDoc.RootElement.GetProperty("response").GetProperty("players");
            if (players.GetArrayLength() > 0)
            {
                var player = players[0];
                result.PersonaName = player.GetProperty("personaname").GetString() ?? "未知";
                result.PersonaState = player.TryGetProperty("personastate", out var state) ? state.GetInt32() : 0;
                result.AvatarUrl = player.TryGetProperty("avatarfull", out var avatar) ? avatar.GetString() ?? "" : "";
                
                if (player.TryGetProperty("gameextrainfo", out var gameInfo))
                    result.PlayingGame = gameInfo.GetString() ?? "";

                if (!string.IsNullOrEmpty(result.AvatarUrl))
                {
                    avatarDownloadTasks.Add(Task.Run(async () => {
                        try { result.Avatar = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(result.AvatarUrl)); } catch { }
                    }));
                }
            }

            // 2. 获取目标玩家最近两周游玩记录
            try
            {
                string recentUrl = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key={ApiKey}&steamid={steamId}";
                var recentJson = await _httpClient.GetStringAsync(recentUrl);
                using var recentDoc = JsonDocument.Parse(recentJson);
                
                if (recentDoc.RootElement.GetProperty("response").TryGetProperty("games", out var gamesArray))
                {
                    foreach (var game in gamesArray.EnumerateArray())
                    {
                        result.RecentGames.Add(new RecentGame
                        {
                            Name = game.GetProperty("name").GetString() ?? "未知",
                            Playtime2Weeks = game.TryGetProperty("playtime_2weeks", out var pt) ? pt.GetInt32() : 0
                        });
                    }
                }
            }
            catch { /* 忽略隐藏了游戏详情的错误 */ }

            // 3. 获取好友列表并筛选最活跃的几个
            try
            {
                string friendListUrl = $"https://api.steampowered.com/ISteamUser/GetFriendList/v0001/?key={ApiKey}&steamid={steamId}&relationship=friend";
                var friendListJson = await _httpClient.GetStringAsync(friendListUrl);
                using var friendListDoc = JsonDocument.Parse(friendListJson);

                var friendIds = new List<string>();
                foreach (var friend in friendListDoc.RootElement.GetProperty("friendslist").GetProperty("friends").EnumerateArray())
                {
                    if (friend.TryGetProperty("steamid", out var fId)) friendIds.Add(fId.GetString() ?? "");
                }

                if (friendIds.Count > 0)
                {
                    // 批量查状态 (最多查 100 个)
                    string commaSeparatedIds = string.Join(",", friendIds.Take(100));
                    string fSummaryUrl = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={ApiKey}&steamids={commaSeparatedIds}";
                    var fSummaryJson = await _httpClient.GetStringAsync(fSummaryUrl);
                    using var fSummaryDoc = JsonDocument.Parse(fSummaryJson);

                    var allFriends = new List<SteamFriendInfo>();
                    foreach (var player in fSummaryDoc.RootElement.GetProperty("response").GetProperty("players").EnumerateArray())
                    {
                        var info = new SteamFriendInfo
                        {
                            PersonaName = player.TryGetProperty("personaname", out var name) ? name.GetString() ?? "未知" : "未知",
                            PersonaState = player.TryGetProperty("personastate", out var state) ? state.GetInt32() : 0,
                            AvatarUrl = player.TryGetProperty("avatar", out var avatar) ? avatar.GetString() ?? "" : "" // 用中等头像省流量
                        };
                        
                        if (player.TryGetProperty("lastlogoff", out var logoff))
                            info.LastLogoff = DateTimeOffset.FromUnixTimeSeconds(logoff.GetInt64()).LocalDateTime;

                        if (player.TryGetProperty("gameextrainfo", out var gameInfo))
                            info.PlayingGame = gameInfo.GetString() ?? "";

                        allFriends.Add(info);
                    }

                    // 核心排序：玩游戏的 > 在线的 > 最近离线的
                    result.TopFriends = allFriends
                        .OrderByDescending(f => !string.IsNullOrEmpty(f.PlayingGame))
                        .ThenByDescending(f => f.PersonaState > 0)
                        .ThenByDescending(f => f.LastLogoff)
                        .Take(6) // 仅仅在图片里展示最活跃的前 6 个好友，防止图片过长
                        .ToList();

                    // 并发下载这 6 个好友的头像
                    foreach (var f in result.TopFriends)
                    {
                        if (!string.IsNullOrEmpty(f.AvatarUrl))
                        {
                            avatarDownloadTasks.Add(Task.Run(async () => {
                                try { f.Avatar = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(f.AvatarUrl)); } catch { }
                            }));
                        }
                    }
                }
            }
            catch (HttpRequestException e) when (e.Message.Contains("401"))
            {
                result.IsFriendListPrivate = true; // 标记好友列表私密
            }

            // 等待所有头像下载完成
            await Task.WhenAll(avatarDownloadTasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[查岗失败] {ex.Message}");
            return null;
        }
        return result;
    }

    // ==========================================
    // 动态渲染：新拟态手绘卡通风监控面板
    // ==========================================
    public static string GenerateFullSpyCardBase64(SteamSpyResult result)
    {
        // 动态计算画布高度
        int width = 800;
        int targetCardHeight = 280; // 目标玩家卡片高度
        int friendRowHeight = 100;  // 每个好友卡片的高度
        int spacing = 20;
        
        // 基础高度 + 目标卡片 + 好友列表标题 + 好友卡片总高度
        int friendsCount = result.TopFriends.Count == 0 ? 1 : result.TopFriends.Count; 
        int height = 150 + targetCardHeight + 80 + (friendsCount * (friendRowHeight + spacing)) + 40;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // 新拟态卡通配色
        var bgColor = SKColor.Parse("#Fdfbf7");      
        var strokeColor = SKColor.Parse("#2d3436");  
        var shadowColor = SKColor.Parse("#e8e1d5");  
        var playingColor = SKColor.Parse("#00b894");   // 游玩中（绿）
        var onlineColor = SKColor.Parse("#0984e3");    // 在线（蓝）
        var offlineColor = SKColor.Parse("#b2bec3");   // 离线（灰）
        var redAlertColor = SKColor.Parse("#ff7675");  // 标题警示红

        canvas.Clear(bgColor);
        
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        using var cardFillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var shadowPaint = new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };

        // ---------------- 绘制标题 ----------------
        canvas.DrawText(" 绝密查岗与雷达报告 ", width / 2f, 70, new SKPaint { Color = redAlertColor, Typeface = typeface, TextSize = 42, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
        canvas.DrawText("正在全网视奸该玩家及其共犯的游玩状态...", width / 2f, 105, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 20, IsAntialias = true, TextAlign = SKTextAlign.Center });
        canvas.DrawLine(width / 2f - 220, 125, width / 2f + 220, 125, strokePaint);

        // ---------------- 绘制目标玩家卡片 ----------------
        float targetY = 150;
        canvas.DrawRoundRect(46, targetY + 6, width - 80, targetCardHeight, 15, 15, shadowPaint);
        canvas.DrawRoundRect(40, targetY, width - 80, targetCardHeight, 15, 15, cardFillPaint);
        canvas.DrawRoundRect(40, targetY, width - 80, targetCardHeight, 15, 15, strokePaint);

        // 目标头像
        var avatarRect = new SKRect(65, targetY + 25, 65 + 130, targetY + 25 + 130);
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

        // 目标名字与状态
        canvas.DrawText(result.PersonaName, 220, targetY + 65, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 38, IsAntialias = true, FakeBoldText = true });
        
        string tStateStr = result.PersonaState > 0 ? "当前在线" : "目前离线";
        SKColor tStateColor = result.PersonaState > 0 ? onlineColor : offlineColor;
        if (!string.IsNullOrEmpty(result.PlayingGame))
        {
            tStateStr = $"正在游玩: {result.PlayingGame}";
            tStateColor = playingColor;
        }
        canvas.DrawText(tStateStr, 220, targetY + 115, new SKPaint { Color = tStateColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });

        // 最近两周记录 (在卡片右下侧)
        canvas.DrawText("最近两周肝度：", 220, targetY + 175, new SKPaint { Color = offlineColor, Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true });
        if (result.RecentGames.Count == 0)
        {
            canvas.DrawText("该玩家最近似乎没碰过游戏，或者隐藏了动态。", 220, targetY + 210, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 20, IsAntialias = true });
        }
        else
        {
            float gameY = targetY + 210;
            for (int i = 0; i < Math.Min(3, result.RecentGames.Count); i++)
            {
                var g = result.RecentGames[i];
                string gText = $"- {g.Name} ({(g.Playtime2Weeks / 60.0):0.1} 小时)";
                canvas.DrawText(gText, 220, gameY, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 20, IsAntialias = true });
                gameY += 30;
            }
        }

        // ---------------- 绘制雷达共犯名单 (好友) ----------------
        float radarY = targetY + targetCardHeight + 50;
        canvas.DrawText("活跃共犯名单", 40, radarY, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 28, IsAntialias = true, FakeBoldText = true });
        
        float currentY = radarY + 30;

        if (result.IsFriendListPrivate)
        {
            // 隐藏了好友列表
            canvas.DrawRoundRect(46, currentY + 6, width - 80, friendRowHeight, 10, 10, shadowPaint);
            canvas.DrawRoundRect(40, currentY, width - 80, friendRowHeight, 10, 10, cardFillPaint);
            canvas.DrawRoundRect(40, currentY, width - 80, friendRowHeight, 10, 10, strokePaint);
            canvas.DrawText("该玩家设置了好友列表私密，无法探测共犯！", 60, currentY + 55, new SKPaint { Color = redAlertColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });
        }
        else if (result.TopFriends.Count == 0)
        {
            canvas.DrawText("这人居然没有好友，或者他的好友全都在离线装死...", 40, currentY + 40, new SKPaint { Color = offlineColor, Typeface = typeface, TextSize = 22, IsAntialias = true });
        }
        else
        {
            // 循环画好友卡片
            foreach (var f in result.TopFriends)
            {
                canvas.DrawRoundRect(46, currentY + 4, width - 80, friendRowHeight, 10, 10, shadowPaint);
                canvas.DrawRoundRect(40, currentY, width - 80, friendRowHeight, 10, 10, cardFillPaint);
                canvas.DrawRoundRect(40, currentY, width - 80, friendRowHeight, 10, 10, strokePaint);

                // 好友头像
                var fAvatarRect = new SKRect(55, currentY + 15, 55 + 70, currentY + 15 + 70);
                if (f.Avatar != null)
                {
                    canvas.Save();
                    var clipPath = new SKPath();
                    clipPath.AddRoundRect(fAvatarRect, 8, 8);
                    canvas.ClipPath(clipPath);
                    canvas.DrawBitmap(f.Avatar, fAvatarRect);
                    canvas.Restore();
                }
                canvas.DrawRoundRect(fAvatarRect, 8, 8, strokePaint);

                // 好友名字 (截断长名字)
                string fName = f.PersonaName.Length > 15 ? f.PersonaName.Substring(0, 14) + "..." : f.PersonaName;
                canvas.DrawText(fName, 145, currentY + 45, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });

                // 好友状态
                string fStateStr = f.PersonaState > 0 ? "在线" : $"离线 ({f.LastLogoff:MM-dd HH:mm})";
                SKColor fStateColor = f.PersonaState > 0 ? onlineColor : offlineColor;
                if (!string.IsNullOrEmpty(f.PlayingGame))
                {
                    fStateStr = $"正在肝: {f.PlayingGame}";
                    fStateColor = playingColor;
                }
                canvas.DrawText(fStateStr, 145, currentY + 80, new SKPaint { Color = fStateColor, Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true });

                currentY += (friendRowHeight + spacing);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}