using System.Text.Json;
using SkiaSharp;

namespace MilkyQQBot;

public class SteamGameInfo
{
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public int DiscountPercent { get; set; }
    public double OriginalPrice { get; set; }
    public double FinalPrice { get; set; }
    public SKBitmap? Thumbnail { get; set; }
}

public static class SteamHotService
{
    private static readonly HttpClient _httpClient = new();
    // Steam 官方公开商店 API (国区, 简体中文)
    private const string SteamApiUrl = "https://store.steampowered.com/api/featuredcategories/?cc=cn&l=schinese";

    // 1. 获取并解析 Steam 真实数据
    public static async Task<List<SteamGameInfo>> GetTopSellersAsync()
    {
        var games = new List<SteamGameInfo>();
        try
        {
            // 伪装一下 User-Agent，防止被 Steam 拦截
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            var json = await _httpClient.GetStringAsync(SteamApiUrl);
            using var doc = JsonDocument.Parse(json);

            var items = doc.RootElement.GetProperty("top_sellers").GetProperty("items").EnumerateArray();
            
            foreach (var item in items)
            {
                if (games.Count >= 8) break; // 只取前 8 款游戏

                var game = new SteamGameInfo
                {
                    Name = item.GetProperty("name").GetString() ?? "未知游戏",
                    // large_capsule_image 尺寸和比例最适合做卡片展示
                    ImageUrl = item.GetProperty("large_capsule_image").GetString() ?? "",
                    DiscountPercent = item.TryGetProperty("discount_percent", out var dp) ? dp.GetInt32() : 0,
                    // Steam API 价格单位是“分”，需要除以 100
                    OriginalPrice = item.TryGetProperty("original_price", out var op) ? op.GetInt32() / 100.0 : 0,
                    FinalPrice = item.TryGetProperty("final_price", out var fp) ? fp.GetInt32() / 100.0 : 0
                };
                games.Add(game);
            }

            // 并发下载这 8 款游戏的封面图
            var downloadTasks = games.Select(async g =>
            {
                if (!string.IsNullOrEmpty(g.ImageUrl))
                {
                    try
                    {
                        var imgBytes = await _httpClient.GetByteArrayAsync(g.ImageUrl);
                        g.Thumbnail = SKBitmap.Decode(imgBytes);
                    }
                    catch { /* 忽略单张图片下载失败 */ }
                }
            });
            await Task.WhenAll(downloadTasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Steam数据获取失败] {ex.Message}");
        }

        return games;
    }

    // 2. 动态渲染手绘卡通风榜单图片 (【全新单列宽屏排版】)
    public static string GenerateBase64Image(List<SteamGameInfo> games)
    {
        // 画布尺寸设置 (单列排版，8行)
        int width = 800;
        int headerHeight = 140;
        int cardWidth = 740;  // 宽度大幅拉伸，一行一个
        int cardHeight = 140; // 高度压缩，使得排版更加紧凑
        int spacingY = 25;    // 垂直间距
        int height = headerHeight + (games.Count * (cardHeight + spacingY)) + 40;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // 配色板 (新拟态卡通手绘风)
        var bgColor = SKColor.Parse("#Fdfbf7");      
        var strokeColor = SKColor.Parse("#2d3436");  
        var shadowColor = SKColor.Parse("#e8e1d5");  
        var discountColor = SKColor.Parse("#ff7675"); 
        var priceColor = SKColor.Parse("#00b894");   
        var originalPriceColor = SKColor.Parse("#b2bec3"); 

        canvas.Clear(bgColor);

        // 字体设置
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        
        using var titlePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 48, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
        using var subtitlePaint = new SKPaint { Color = discountColor, Typeface = typeface, TextSize = 20, IsAntialias = true, TextAlign = SKTextAlign.Center };
        // 游戏名重回大号字体
        using var namePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true };
        using var pricePaint = new SKPaint { Color = priceColor, Typeface = typeface, TextSize = 32, IsAntialias = true, FakeBoldText = true };
        using var originalPricePaint = new SKPaint { Color = originalPriceColor, Typeface = typeface, TextSize = 20, IsAntialias = true };
        using var originalPriceStrikePaint = new SKPaint { Color = originalPriceColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        using var badgeTextPaint = new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = 22, IsAntialias = true, FakeBoldText = true };
        
        using var cardFillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var shadowPaint = new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var badgeFillPaint = new SKPaint { Color = discountColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };

        // 绘制大标题和可爱的装饰线
        canvas.DrawText("  Steam 实时热销榜单  ", width / 2f, 70, titlePaint);
        canvas.DrawText("看看大家都在为哪些游戏剁手 (国区)", width / 2f, 105, subtitlePaint);
        var path = new SKPath();
        path.MoveTo(width / 2f - 180, 125);
        path.QuadTo(width / 2f, 140, width / 2f + 180, 125);
        canvas.DrawPath(path, strokePaint);

        // 循环绘制 8 个游戏卡片
        for (int i = 0; i < games.Count; i++)
        {
            var game = games[i];
            
            // X 固定居中，Y 随行数递增
            float x = 30;
            float y = headerHeight + i * (cardHeight + spacingY);

            // A: 画卡片的底座硬阴影
            canvas.DrawRoundRect(x + 6, y + 6, cardWidth, cardHeight, 15, 15, shadowPaint);
            
            // B: 画卡片白色主体和粗黑描边
            canvas.DrawRoundRect(x, y, cardWidth, cardHeight, 15, 15, cardFillPaint);
            canvas.DrawRoundRect(x, y, cardWidth, cardHeight, 15, 15, strokePaint);

            // C: 绘制游戏缩略图 (采用 Steam 标准宽屏比例 460:215，缩小映射为 214:100)
            float imgX = x + 20;
            float imgY = y + 20;
            float imgW = 214;
            float imgH = 100;
            var imgRect = new SKRect(imgX, imgY, imgX + imgW, imgY + imgH);
            
            if (game.Thumbnail != null)
            {
                canvas.Save();
                var clipPath = new SKPath();
                clipPath.AddRoundRect(imgRect, 8, 8);
                canvas.ClipPath(clipPath);
                canvas.DrawBitmap(game.Thumbnail, imgRect);
                canvas.Restore();
            }
            canvas.DrawRoundRect(imgRect, 8, 8, strokePaint);

            // D: 绘制排名角标 (左上角)
            canvas.DrawCircle(x, y, 20, cardFillPaint);
            canvas.DrawCircle(x, y, 20, strokePaint);
            canvas.DrawText((i + 1).ToString(), x - 7, y + 8, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });

            // E: 绘制游戏名字 (横向空间极其充裕！)
            float textX = imgX + imgW + 25;
            float maxTextWidth = cardWidth - (imgW + 65); // 留出右侧安全边距
            string displayName = game.Name;
            
            if (namePaint.MeasureText(displayName) > maxTextWidth)
            {
                while (namePaint.MeasureText(displayName + "...") > maxTextWidth && displayName.Length > 0)
                {
                    displayName = displayName.Substring(0, displayName.Length - 1);
                }
                displayName += "...";
            }
            // 名字放高一点
            canvas.DrawText(displayName, textX, imgY + 35, namePaint);

            // F: 同行排列的价格与折扣区
            float priceY = imgY + 85; 
            
            if (game.DiscountPercent > 0)
            {
                // 1. 折扣徽章 (-XX%)
                float badgeW = 75;
                float badgeH = 34;
                var badgeRect = new SKRect(textX, priceY - 26, textX + badgeW, priceY + badgeH - 26);
                canvas.DrawRoundRect(badgeRect, 6, 6, badgeFillPaint);
                canvas.DrawRoundRect(badgeRect, 6, 6, strokePaint);
                
                // 居中绘制折扣文字
                var badgeTextBounds = new SKRect();
                string discountText = $"-{game.DiscountPercent}%";
                badgeTextPaint.MeasureText(discountText, ref badgeTextBounds);
                canvas.DrawText(discountText, textX + (badgeW - badgeTextBounds.Width) / 2, priceY - 2, badgeTextPaint);

                // 2. 原价 (放徽章右边)
                string origPriceStr = $"¥ {game.OriginalPrice:0.00}";
                float origX = textX + badgeW + 15;
                canvas.DrawText(origPriceStr, origX, priceY, originalPricePaint);
                
                // 原价删除线
                float origWidth = originalPricePaint.MeasureText(origPriceStr);
                canvas.DrawLine(origX - 2, priceY - 7, origX + origWidth + 2, priceY - 7, originalPriceStrikePaint);

                // 3. 现价 (紧接着原价放右边)
                float finalPriceX = origX + origWidth + 20;
                canvas.DrawText($"¥ {game.FinalPrice:0.00}", finalPriceX, priceY + 2, pricePaint);
            }
            else
            {
                // 无折扣：只显示纯原价或免费
                if (game.FinalPrice == 0)
                {
                    canvas.DrawText("免费开玩", textX, priceY + 2, pricePaint);
                }
                else
                {
                    canvas.DrawText($"¥ {game.FinalPrice:0.00}", textX, priceY + 2, pricePaint);
                }
            }
        }

        // 导出 Base64
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}