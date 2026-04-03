using SkiaSharp;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System;
using System.Collections.Generic;

namespace MilkyQQBot;

public class L4d2StatsResult
{
    public string PersonaName { get; set; } = "未知幸存者";
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
    
    // 核心战绩数据
    public int TotalKills { get; set; }    // 丧尸总杀敌 (真实累加)
    public int MeleeKills { get; set; }    // 近战总击杀
    public int Headshots { get; set; }     // 爆头总数
    public int TeamRevived { get; set; }   // 救起队友次数
    public int WasRevived { get; set; }    // 倒地被拉次数
    public int FFDamage { get; set; }      // 痛击队友伤害
    
    public bool IsPrivate { get; set; }
    public bool HasNoStats { get; set; }
}

public static class L4d2StatsService
{
    private static readonly HttpClient _httpClient = new();
    private const string ApiKey = "你的 Steam API Key";

    static L4d2StatsService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public static async Task<L4d2StatsResult> GetStatsAsync(string steamId)
    {
        var result = new L4d2StatsResult();

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
                    try { result.Avatar = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(result.AvatarUrl)); } catch { }
                }
            }

            // 2. 获取 L4D2 详细统计
            string statsUrl = $"https://api.steampowered.com/ISteamUserStats/GetUserStatsForGame/v0002/?appid=550&key={ApiKey}&steamid={steamId}";
            var res = await _httpClient.GetAsync(statsUrl);

            if (res.StatusCode == System.Net.HttpStatusCode.Forbidden || res.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                result.IsPrivate = true; 
                return result;
            }
            if (res.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                result.HasNoStats = true; 
                return result;
            }

            var statsJson = await res.Content.ReadAsStringAsync();
            using var statsDoc = JsonDocument.Parse(statsJson);
            
            if (statsDoc.RootElement.TryGetProperty("playerstats", out var playerStats))
            {
                if (playerStats.TryGetProperty("stats", out var statsArray))
                {
                    // 近战武器名录
                    var meleeWeapons = new HashSet<string> { "katana", "fireaxe", "machete", "baseball_bat", "crowbar", "frying_pan", "chainsaw", "golfclub", "cricket_bat", "electric_guitar", "pitchfork", "shovel", "knife", "tonfa" };

                    foreach (var stat in statsArray.EnumerateArray())
                    {
                        string name = stat.GetProperty("name").GetString() ?? "";
                        int value = 0;
                        if (stat.TryGetProperty("value", out var valElement) && valElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            value = (int)valElement.GetDouble();
                        }

                        // 精准匹配单项数据
                        if (name == "Stat.TeamRevived.Total") result.TeamRevived = value;
                        else if (name == "Stat.WasRevived.Total") result.WasRevived = value;
                        else if (name == "Stat.FFDamage.Total") result.FFDamage = value;
                        
                        // 累加爆头总数
                        else if (name.EndsWith(".Head.Total")) 
                        {
                            result.Headshots += value;
                        }
                        
                        // 【终极修复】无视官方错误的总计字段，暴力累加所有武器的真实击杀！
                        else if (name.EndsWith(".Kills.Total"))
                        {
                            // 排除弹药类型的击杀防止双重计算
                            if (!name.Contains("ammo")) 
                            {
                                result.TotalKills += value;
                            }
                            
                            // 累加近战武器击杀
                            string weaponName = name.Replace("Stat.", "").Replace(".Kills.Total", "");
                            if (meleeWeapons.Contains(weaponName))
                            {
                                result.MeleeKills += value;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[L4D2战绩获取失败] {ex.Message}");
            return null;
        }

        return result;
    }

    // ==========================================
    // 动态渲染：暗黑生化风 + 超宽排版
    // ==========================================
    public static string GenerateCedaReportBase64(L4d2StatsResult result)
    {
        // 画布加宽至 920，保证千万级数字也绝不重叠
        int width = 920; 
        int height = 540;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // 全新暗黑配色卡
        var bgColor = SKColor.Parse("#121212");         // 深渊黑底色
        var textColor = SKColor.Parse("#e0e0e0");       // 高对比主字体
        var labelColor = SKColor.Parse("#9e9e9e");      // 说明标签色
        var redInkColor = SKColor.Parse("#ff5252");     // 刺眼荧光红
        var fadedLineColor = SKColor.Parse("#333333");  // 暗色分割线
        
        canvas.Clear(bgColor);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        // 极具质感的暗色干涸血迹
        var random = new Random();
        using var bloodPaint = new SKPaint { Color = SKColor.Parse("#4a0000").WithAlpha(90), Style = SKPaintStyle.Fill, IsAntialias = true };
        for (int i = 0; i < 8; i++)
        {
            canvas.DrawCircle(random.Next(0, width), random.Next(0, height), random.Next(20, 80), bloodPaint);
        }

        // 头部标题
        canvas.DrawText("C.E.D.A. 感染控制局 - 幸存者机密档案", 40, 60, new SKPaint { Color = textColor, Typeface = typeface, TextSize = 32, IsAntialias = true, FakeBoldText = true });
        canvas.DrawLine(40, 80, width - 40, 80, new SKPaint { Color = textColor, StrokeWidth = 4, IsAntialias = true });
        canvas.DrawLine(40, 86, width - 40, 86, new SKPaint { Color = textColor, StrokeWidth = 2, IsAntialias = true });

        if (result.IsPrivate || result.HasNoStats)
        {
            string errorText = result.IsPrivate ? "⚠️ 访问受限：该人员档案被军方加密 (主页私密)" : "⚠️ 查无此人：该目标从未踏足过疫区 (未游玩L4D2)";
            canvas.DrawText(errorText, 40, 160, new SKPaint { Color = redInkColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });
            using var img = SKImage.FromBitmap(bitmap);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            return $"base64://{Convert.ToBase64String(d.ToArray())}";
        }

        // 证件照绘制
        var avatarRect = new SKRect(40, 120, 190, 270);
        if (result.Avatar != null)
        {
            canvas.DrawBitmap(result.Avatar, avatarRect);
            canvas.DrawRect(40, 245, 150, 25, new SKPaint { Color = SKColor.Parse("#00c853").WithAlpha(200), Style = SKPaintStyle.Fill });
            canvas.DrawText("UNINFECTED", 65, 263, new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = 16, IsAntialias = true, FakeBoldText = true });
        }
        canvas.DrawRect(avatarRect, new SKPaint { Color = textColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true });

        // 基础信息
        float rightX = 230; 
        float currentY = 150;
        canvas.DrawText($"被评估人: {result.PersonaName}", rightX, currentY, new SKPaint { Color = textColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });
        currentY += 40;
        canvas.DrawText("感染状态: 隐性免疫携带者 (安全)", rightX, currentY, new SKPaint { Color = SKColor.Parse("#69f0ae"), Typeface = typeface, TextSize = 22, IsAntialias = true, FakeBoldText = true });
        
        currentY += 30;
        using var dashPaint = new SKPaint { Color = fadedLineColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash(new float[] { 10, 10 }, 0) };
        canvas.DrawLine(rightX, currentY, width - 40, currentY, dashPaint);

        // ==========================================
        // 数据排版：超宽列距，强对比荧光色
        // ==========================================
        currentY += 40;
        var labelStyle = new SKPaint { Color = labelColor, Typeface = typeface, TextSize = 20, IsAntialias = true };
        var valueStyle = new SKPaint { Color = textColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true };

        // 极其宽敞的 X 轴锚点
        float col1LabelX = rightX;
        float col1ValueX = rightX + 145; 
        float col2LabelX = rightX + 340; 
        float col2ValueX = rightX + 485; 

        // 第一排
        canvas.DrawText("屠宰丧尸总数:", col1LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.TotalKills:N0}", col1ValueX, currentY, valueStyle);

        canvas.DrawText("近战肉搏击杀:", col2LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.MeleeKills:N0}", col2ValueX, currentY, valueStyle);

        // 第二排
        currentY += 50;
        canvas.DrawText("精准爆头总数:", col1LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.Headshots:N0}", col1ValueX, currentY, valueStyle);

        canvas.DrawText("救起倒地队友:", col2LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.TeamRevived:N0}", col2ValueX, currentY, new SKPaint { Color = SKColor.Parse("#448aff"), Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });

        // 第三排
        currentY += 50;
        canvas.DrawText("倒地被救次数:", col1LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.WasRevived:N0}", col1ValueX, currentY, new SKPaint { Color = SKColor.Parse("#ffb74d"), Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });

        canvas.DrawText("痛击队友伤害:", col2LabelX, currentY, labelStyle);
        canvas.DrawText($"{result.FFDamage:N0}", col2ValueX, currentY, new SKPaint { Color = redInkColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });


        // ==========================================
        // 终极评价印章
        // ==========================================
        string diagnosisTitle = "炮灰幸存者";
        string diagnosisDesc = "建议呆在安全屋里不要出来。";
        SKColor stampColor = SKColor.Parse("#757575"); 

        if (result.TotalKills > 0)
        {
            if (result.FFDamage > 500000) 
            {
                diagnosisTitle = "王牌二五仔";
                diagnosisDesc = "极度危险分子！打队友比打丧尸还准！";
                stampColor = redInkColor;
            }
            else if (result.WasRevived > result.TeamRevived * 2 && result.WasRevived > 1000)
            {
                diagnosisTitle = "重度轮椅人";
                diagnosisDesc = "全队的累赘，不是在倒地就是在倒地的路上。";
                stampColor = SKColor.Parse("#b388ff"); // 霓虹紫
            }
            else if (result.TeamRevived > 5000) 
            {
                diagnosisTitle = "赛博华佗";
                diagnosisDesc = "真正的天使，遇到这种队友请立刻抱紧大腿！";
                stampColor = SKColor.Parse("#69f0ae"); // 荧光绿
            }
            else if (result.TotalKills > 100000)
            {
                diagnosisTitle = "末日老兵";
                diagnosisDesc = "在这个满是丧尸的世界里杀了七进七出。";
                stampColor = SKColor.Parse("#448aff"); // 科技蓝
            }
        }

        // 盖章动画效果 (将印章挪到了右侧空白处，不会遮挡任何文字)
        canvas.Save();
        canvas.Translate(rightX + 330, currentY + 70); 
        canvas.RotateDegrees(-12); 
        
        var stampRect = new SKRect(-155, -40, 155, 40);
        canvas.DrawRoundRect(stampRect, 10, 10, new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true });
        canvas.DrawRoundRect(new SKRect(-147, -32, 147, 32), 6, 6, new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true });
        
        canvas.DrawText(diagnosisTitle, 0, -5, new SKPaint { Color = stampColor, Typeface = typeface, TextSize = 34, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
        canvas.DrawText(diagnosisDesc, 0, 20, new SKPaint { Color = stampColor, Typeface = typeface, TextSize = 15, IsAntialias = true, TextAlign = SKTextAlign.Center });
        
        canvas.Restore();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}