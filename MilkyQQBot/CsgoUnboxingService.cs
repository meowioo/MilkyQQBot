using SkiaSharp;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System;

namespace MilkyQQBot;

public class UnboxResult
{
    public string SkinName { get; set; } = "";
    public string RarityName { get; set; } = "";
    public string ColorHex { get; set; } = "";
    public double DropRate { get; set; }
    public bool IsRare { get; set; } 
    public SKBitmap? Image { get; set; } 
}

public static class CsgoUnboxingService
{
    private static readonly Random _rnd = new();
    private static readonly HttpClient _httpClient = new();

    // 终极绝招：将下载好的图片字节直接缓存在内存中！
    // 只要第一次下载成功，以后同一把枪0网络请求，光速秒出图，彻底免疫防爬虫机制！
    private static readonly ConcurrentDictionary<string, byte[]> _imageCache = new();

    static CsgoUnboxingService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    private static readonly string[] GoldSkins = { "★ 蝴蝶刀 | 传说", "★ 爪子刀 | 渐变之色", "★ 迈阿密风云 (运动手套)", "★ M9 刺刀 | 多普勒", "★ 骷髅匕首 | 渐变大理石", "★ 折叠刀 | 屠夫", "★ 潘多拉之盒 (运动手套)" };
    private static readonly string[] RedSkins = { "AWP | 巨龙传说", "AK-47 | 火神", "M4A4 | 咆哮", "AWP | 二西莫夫", "沙漠之鹰 | 印花集", "AK-47 | 皇后", "M4A1 消音版 | 印花集", "Glock-18 | 渐变之色" };
    private static readonly string[] PinkSkins = { "AK-47 | 红线", "M4A1 消音版 | 毁灭者 2000", "AWP | 树蝰", "USP 消音版 | 脑洞大开", "MAC-10 | 霓虹骑士", "沙漠之鹰 | 机械工业", "M4A4 | 地狱火" };
    private static readonly string[] PurpleSkins = { "AK-47 | 墨岩", "M4A4 | 镁合金", "AWP | 蠕虫之神", "沙漠之鹰 | 陨星", "USP 消音版 | 蓝图", "Glock-18 | 皇家军团", "AUG | 变色龙" };
    private static readonly string[] BlueSkins = { "P250 | 沙丘 (传世经典)", "法玛斯 | 殖民地", "MP7 | 陆军侦察兵", "P90 | 沙漠战争", "Tec-9 | 冰能", "Nova | 胡桃木", "SG 553 | 陆军光泽", "Glock-18 | 地下水" };

    // 必须包含磨损度 (Field-Tested / Factory New) 的极其精准的官方全名
    private static readonly Dictionary<string, string> SkinExactHashNames = new()
    {
        // 金
        {"★ 蝴蝶刀 | 传说", "★ Butterfly Knife | Lore (Field-Tested)"},
        {"★ 爪子刀 | 渐变之色", "★ Karambit | Fade (Factory New)"},
        {"★ 迈阿密风云 (运动手套)", "★ Sport Gloves | Vice (Field-Tested)"},
        {"★ M9 刺刀 | 多普勒", "★ M9 Bayonet | Doppler (Factory New)"},
        {"★ 骷髅匕首 | 渐变大理石", "★ Skeleton Knife | Marble Fade (Factory New)"},
        {"★ 折叠刀 | 屠夫", "★ Flip Knife | Slaughter (Factory New)"},
        {"★ 潘多拉之盒 (运动手套)", "★ Sport Gloves | Pandora's Box (Field-Tested)"},
        
        // 红
        {"AWP | 巨龙传说", "AWP | Dragon Lore (Field-Tested)"},
        {"AK-47 | 火神", "AK-47 | Vulcan (Field-Tested)"},
        {"M4A4 | 咆哮", "M4A4 | Howl (Field-Tested)"},
        {"AWP | 二西莫夫", "AWP | Asiimov (Field-Tested)"},
        {"沙漠之鹰 | 印花集", "Desert Eagle | Printstream (Field-Tested)"},
        {"AK-47 | 皇后", "AK-47 | The Empress (Field-Tested)"},
        {"M4A1 消音版 | 印花集", "M4A1-S | Printstream (Field-Tested)"},
        {"Glock-18 | 渐变之色", "Glock-18 | Fade (Factory New)"},
        
        // 粉
        {"AK-47 | 红线", "AK-47 | Redline (Field-Tested)"},
        {"M4A1 消音版 | 毁灭者 2000", "M4A1-S | Decimator (Field-Tested)"},
        {"AWP | 树蝰", "AWP | Atheris (Field-Tested)"},
        {"USP 消音版 | 脑洞大开", "USP-S | Cortex (Field-Tested)"},
        {"MAC-10 | 霓虹骑士", "MAC-10 | Neon Rider (Field-Tested)"},
        {"沙漠之鹰 | 机械工业", "Desert Eagle | Mecha Industries (Field-Tested)"},
        {"M4A4 | 地狱火", "M4A4 | Hellfire (Field-Tested)"},
        
        // 紫
        {"AK-47 | 墨岩", "AK-47 | Slate (Field-Tested)"},
        {"M4A4 | 镁合金", "M4A4 | Magnesium (Field-Tested)"},
        {"AWP | 蠕虫之神", "AWP | Worm God (Field-Tested)"},
        {"沙漠之鹰 | 陨星", "Desert Eagle | Meteorite (Factory New)"},
        {"USP 消音版 | 蓝图", "USP-S | Blueprint (Field-Tested)"},
        {"Glock-18 | 皇家军团", "Glock-18 | Royal Legion (Field-Tested)"},
        {"AUG | 变色龙", "AUG | Chameleon (Field-Tested)"},
        
        // 蓝
        {"P250 | 沙丘 (传世经典)", "P250 | Sand Dune (Field-Tested)"},
        {"法玛斯 | 殖民地", "FAMAS | Colony (Field-Tested)"},
        {"MP7 | 陆军侦察兵", "MP7 | Army Recon (Field-Tested)"},
        {"P90 | 沙漠战争", "P90 | Desert Warfare (Field-Tested)"},
        {"Tec-9 | 冰能", "Tec-9 | Ice Cap (Field-Tested)"},
        {"Nova | 胡桃木", "Nova | Walnut (Field-Tested)"},
        {"SG 553 | 陆军光泽", "SG 553 | Army Sheen (Field-Tested)"},
        {"Glock-18 | 地下水", "Glock-18 | Groundwater (Field-Tested)"}
    };

    public static async Task<UnboxResult> OpenCaseAsync()
    {
        int roll = _rnd.Next(0, 10000);
        var result = new UnboxResult();

        if (roll < 26) // 金
        {
            result.SkinName = GoldSkins[_rnd.Next(GoldSkins.Length)];
            result.RarityName = "罕见特殊物品";
            result.ColorHex = "#FFD700";
            result.DropRate = 0.26;
            result.IsRare = true;
        }
        else if (roll < 90) // 红
        {
            result.SkinName = RedSkins[_rnd.Next(RedSkins.Length)];
            result.RarityName = "隐秘级 (红)";
            result.ColorHex = "#EB4B4B";
            result.DropRate = 0.64;
            result.IsRare = true;
        }
        else if (roll < 410) // 粉
        {
            result.SkinName = PinkSkins[_rnd.Next(PinkSkins.Length)];
            result.RarityName = "保密级 (粉)";
            result.ColorHex = "#D32CE6";
            result.DropRate = 3.20;
            result.IsRare = false;
        }
        else if (roll < 2008) // 紫
        {
            result.SkinName = PurpleSkins[_rnd.Next(PurpleSkins.Length)];
            result.RarityName = "受限级 (紫)";
            result.ColorHex = "#8847FF";
            result.DropRate = 15.98;
            result.IsRare = false;
        }
        else // 蓝
        {
            result.SkinName = BlueSkins[_rnd.Next(BlueSkins.Length)];
            result.RarityName = "军规级 (蓝)";
            result.ColorHex = "#4B69FF";
            result.DropRate = 79.92;
            result.IsRare = false;
        }

        // ==========================================
        // 直接请求接口获取真身图片，并加入内存图片池
        // ==========================================
        if (SkinExactHashNames.TryGetValue(result.SkinName, out var exactName))
        {
            try
            {
                // 1. 如果内存里已经下过这张图了，直接秒出！
                if (_imageCache.TryGetValue(exactName, out var imgBytes))
                {
                    result.Image = SKBitmap.Decode(imgBytes);
                    Console.WriteLine($"[命中缓存] {result.SkinName} -> 光速出图！");
                }
                // 2. 如果是第一次抽到，去请求重定向接口下载
                else
                {
                    string imgUrl = $"https://api.steamapis.com/image/item/730/{Uri.EscapeDataString(exactName)}";
                    imgBytes = await _httpClient.GetByteArrayAsync(imgUrl);
                    
                    _imageCache[exactName] = imgBytes; // 存入本地内存池
                    result.Image = SKBitmap.Decode(imgBytes);
                    Console.WriteLine($"[图片下载成功] 恭喜抽出：{result.SkinName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[图片获取失败] {ex.Message}");
            }
        }

        return result;
    }

    // ==========================================
    // 动态渲染：新拟态手绘风 "开箱高光卡片"
    // ==========================================
    public static string GenerateUnboxImageBase64(UnboxResult result, string userName)
    {
        int width = 800;
        int height = 580; 

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var bgColor = SKColor.Parse("#1a1b26");
        var panelColor = SKColor.Parse("#24283b");
        var rarityColor = SKColor.Parse(result.ColorHex);
        
        canvas.Clear(bgColor);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        // 1. 绘制底板与高光发光效果
        float cardX = 40, cardY = 40, cardW = width - 80, cardH = height - 80;
        
        if (result.IsRare)
        {
            using var glowPaint = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(width / 2f, height / 2f),
                    450,
                    new[] { rarityColor.WithAlpha(120), rarityColor.WithAlpha(0) },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(0, 0, width, height, glowPaint);
        }

        canvas.DrawRoundRect(cardX + 10, cardY + 10, cardW, cardH, 15, 15, new SKPaint { Color = SKColors.Black.WithAlpha(100), IsAntialias = true });
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, 15, 15, new SKPaint { Color = panelColor, Style = SKPaintStyle.Fill, IsAntialias = true });
        canvas.DrawRoundRect(cardX, cardY, cardW, cardH, 15, 15, new SKPaint { Color = rarityColor, Style = SKPaintStyle.Stroke, StrokeWidth = 8, IsAntialias = true });

        // 2. 顶部文本
        canvas.DrawText("穷鬼武器箱 开启结果", width / 2f, cardY + 50, new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = 28, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
        canvas.DrawLine(cardX + 40, cardY + 75, cardX + cardW - 40, cardY + 75, new SKPaint { Color = rarityColor.WithAlpha(150), StrokeWidth = 3, IsAntialias = true });

        // 3. 核心大图：将下载好的武器图画在正中央
        float currentY = cardY + 95;
        if (result.Image != null)
        {
            float imgW = 280;
            float imgH = 210;
            float imgX = (width - imgW) / 2f;
            
            // 为了让图更亮眼，给图下面垫一点发光阴影
            using var imgGlow = new SKPaint { Shader = SKShader.CreateRadialGradient(new SKPoint(width / 2f, currentY + imgH / 2f), 150, new[] { rarityColor.WithAlpha(50), SKColors.Transparent }, null, SKShaderTileMode.Clamp) };
            canvas.DrawCircle(width / 2f, currentY + imgH / 2f, 150, imgGlow);

            var srcRect = new SKRect(0, 0, result.Image.Width, result.Image.Height);
            var destRect = new SKRect(imgX, currentY, imgX + imgW, currentY + imgH);
            canvas.DrawBitmap(result.Image, srcRect, destRect);
            
            currentY += imgH + 30; 
        }
        else
        {
            canvas.DrawText("[高清图片加载失败]", width / 2f, currentY + 100, new SKPaint { Color = SKColor.Parse("#565f89"), Typeface = typeface, TextSize = 20, IsAntialias = true, TextAlign = SKTextAlign.Center });
            currentY += 180;
        }

        // 4. 文字标题：武器名称
        string titlePrefix = result.IsRare ? "!!! 金光闪闪 !!!" : "获得了";
        canvas.DrawText(titlePrefix, width / 2f, currentY, new SKPaint { Color = SKColor.Parse("#a9b1d6"), Typeface = typeface, TextSize = 22, IsAntialias = true, TextAlign = SKTextAlign.Center });
        
        float skinTextSize = result.SkinName.Length > 15 ? 40 : 50;
        canvas.DrawText(result.SkinName, width / 2f, currentY + 60, new SKPaint { Color = rarityColor, Typeface = typeface, TextSize = skinTextSize, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });

        // 5. 稀有度颜色标签
        float badgeW = 200;
        float badgeH = 40;
        float badgeX = (width - badgeW) / 2f;
        float badgeY = currentY + 95;

        canvas.DrawRoundRect(badgeX, badgeY, badgeW, badgeH, 6, 6, new SKPaint { Color = rarityColor, Style = SKPaintStyle.Fill, IsAntialias = true });
        canvas.DrawText(result.RarityName, width / 2f, badgeY + 28, new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = 22, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });

        // 6. 底部信息：开箱者与爆率
        canvas.DrawText($"开箱人: {userName}", cardX + 30, cardY + cardH - 30, new SKPaint { Color = SKColor.Parse("#c0caf5"), Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true });
        
        var ratePaint = new SKPaint { Color = SKColor.Parse("#7aa2f7"), Typeface = typeface, TextSize = 18, IsAntialias = true, TextAlign = SKTextAlign.Right };
        canvas.DrawText($"该品质官方爆率: {result.DropRate}%", cardX + cardW - 30, cardY + cardH - 30, ratePaint);

        // 狗托认证印章
        if (result.DropRate == 0.26)
        {
            canvas.Save();
            canvas.Translate(cardX + cardW - 140, cardY + cardH - 120);
            canvas.RotateDegrees(-20);
            var stampRect = new SKRect(-70, -35, 70, 15);
            canvas.DrawRoundRect(stampRect, 8, 8, new SKPaint { Color = SKColor.Parse("#FFD700"), Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true });
            canvas.DrawText("狗托认证", 0, -8, new SKPaint { Color = SKColor.Parse("#FFD700"), Typeface = typeface, TextSize = 34, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center });
            canvas.Restore();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}