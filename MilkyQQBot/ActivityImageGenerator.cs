using SkiaSharp;
using System.Net.Http;
using System.IO;

namespace MilkyQQBot;

public static class ActivityImageGenerator
{
    private static readonly HttpClient _httpClient = new();

    public static async Task<string> GenerateBase64ImageAsync(List<UserActivityStat> topUsers, string titleText)
    {
        // 1. 画布尺寸与排版参数
        int width = 850;
        int cardHeight = 100; // 每张数据卡片的高度
        int spacing = 25;     // 卡片之间的间距
        int height = 130 + (topUsers.Count * (cardHeight + spacing)) + 40; // 动态总高度

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // 2. 现代卡通风配色板
        var bgColor = SKColor.Parse("#F4F1EA");      // 奶油米色背景
        var cardColor = SKColors.White;              // 卡片纯白
        var strokeColor = SKColor.Parse("#2D3436");  // 卡通描边深灰色（比纯黑更柔和）
        var shadowColor = SKColor.Parse("#DFD9CF");  // 硬阴影颜色
        var statTextColor = SKColor.Parse("#636E72"); // 统计数据灰色

        // 填充背景
        canvas.Clear(bgColor);

        // 3. 字体设置 (强制加粗以契合卡通漫画感)
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) 
                       ?? SKTypeface.FromFamilyName("SimHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) 
                       ?? SKTypeface.Default;

        // --- 画笔大合集 ---
        using var titlePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 38, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
        using var namePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 28, IsAntialias = true, FakeBoldText = true };
        using var statPaint = new SKPaint { Color = statTextColor, Typeface = typeface, TextSize = 20, IsAntialias = true, FakeBoldText = true };
        
        using var cardFillPaint = new SKPaint { Color = cardColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var shadowPaint = new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        // 灵魂描边画笔
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };

        // 4. 绘制大标题 (绝对居中)
        canvas.DrawText(titleText, width / 2f, 70, titlePaint);

        // 5. 循环绘制“漫画卡片”
        for (int i = 0; i < topUsers.Count; i++)
        {
            var user = topUsers[i];
            
            // 卡片区域计算
            float cardX = 40;
            float cardY = 120 + (i * (cardHeight + spacing));
            float cardW = width - 80;
            float cardRadius = 16;

            // ------------------------------------------
            // 步骤 A: 画卡片的硬阴影 (向右下角偏移 6px)
            // ------------------------------------------
            canvas.DrawRoundRect(cardX + 6, cardY + 6, cardW, cardHeight, cardRadius, cardRadius, shadowPaint);

            // ------------------------------------------
            // 步骤 B: 画卡片主体 (纯白填充 + 粗线条描边)
            // ------------------------------------------
            canvas.DrawRoundRect(cardX, cardY, cardW, cardHeight, cardRadius, cardRadius, cardFillPaint);
            canvas.DrawRoundRect(cardX, cardY, cardW, cardHeight, cardRadius, cardRadius, strokePaint);

            // ------------------------------------------
            // 步骤 C: 绘制名次 (前三名使用特殊马卡龙撞色)
            // ------------------------------------------
            SKColor rankColor = i switch {
                0 => SKColor.Parse("#FF7675"), // 1名: 珊瑚粉红
                1 => SKColor.Parse("#55EFC4"), // 2名: 薄荷青绿
                2 => SKColor.Parse("#FDCB6E"), // 3名: 活力明黄
                _ => SKColor.Parse("#B2BEC3")  // 其他: 银灰色
            };
            using var rankPaint = new SKPaint { Color = rankColor, Typeface = typeface, TextSize = 42, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
            
            // 数字在卡片左侧垂直居中
            float rankX = cardX + 45;
            float textCenterY = cardY + (cardHeight / 2f) + 15; // +15 是粗略的基线补偿
            canvas.DrawText($"{i + 1}", rankX, textCenterY, rankPaint);

            // ------------------------------------------
            // 步骤 D: 绘制带描边的圆形头像
            // ------------------------------------------
            float avatarSize = 64;
            float avatarRadius = avatarSize / 2f;
            float avatarX = cardX + 90;
            float avatarY = cardY + (cardHeight - avatarSize) / 2f; // 垂直居中

            try
            {
                byte[] avatarBytes = await _httpClient.GetByteArrayAsync($"http://q1.qlogo.cn/g?b=qq&nk={user.SenderId}&s=100");
                using var avatarBitmap = SKBitmap.Decode(avatarBytes);
                if (avatarBitmap != null)
                {
                    using var resizedAvatar = avatarBitmap.Resize(new SKImageInfo((int)avatarSize, (int)avatarSize), SKFilterQuality.High);
                    
                    canvas.Save();
                    using var path = new SKPath();
                    path.AddCircle(avatarX + avatarRadius, avatarY + avatarRadius, avatarRadius);
                    canvas.ClipPath(path, SKClipOperation.Intersect, true); 
                    canvas.DrawBitmap(resizedAvatar, avatarX, avatarY); 
                    canvas.Restore(); 
                    
                    // 给头像也加上灵魂粗描边
                    canvas.DrawCircle(avatarX + avatarRadius, avatarY + avatarRadius, avatarRadius, strokePaint);
                }
            }
            catch { /* 防崩溃 */ }

            // ------------------------------------------
            // 步骤 E: 排版文字 (分上下两行显示，显得更精致)
            // ------------------------------------------
            float textX = avatarX + avatarSize + 25;
            
            // 第一行：昵称
            canvas.DrawText(user.Nickname, textX, cardY + 45, namePaint);
            
            // 第二行：数据，使用竖线分隔，干净利落
            string statsText = $"消息: {user.MessageCount}  |  字数: {user.WordCount}  |  图片: {user.ImageCount}  |  表情: {user.FaceCount}";
            canvas.DrawText(statsText, textX, cardY + 80, statPaint);
        }

        // 6. 导出图片
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] imageBytes = data.ToArray();
        
        return $"base64://{Convert.ToBase64String(imageBytes)}";
    }
}