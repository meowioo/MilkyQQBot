using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SkiaSharp;

namespace MilkyQQBot;

public static class AvatarPkService
{
    private static readonly HttpClient _httpClient = new();

    private static string ApiUrl => AppConfig.Current.Ai.AvatarPk.ApiUrl;
    private static string ApiKey => AppConfig.Current.Ai.AvatarPk.ApiKey;
    private static string ModelName => AppConfig.Current.Ai.AvatarPk.Model;

    public static async Task<string> GeneratePkImageBase64Async(string qqA, string nameA, string qqB, string nameB)
    {
        // 强制使用 https，防止网关拦截
        string urlA = $"https://q1.qlogo.cn/g?b=qq&nk={qqA}&s=640";
        string urlB = $"https://q1.qlogo.cn/g?b=qq&nk={qqB}&s=640";

        // ==========================================
        // 1. 优先并发下载两个头像
        // ==========================================
        SKBitmap? avatarA = null;
        SKBitmap? avatarB = null;
        try
        {
            var taskA = _httpClient.GetByteArrayAsync(urlA);
            var taskB = _httpClient.GetByteArrayAsync(urlB);
            await Task.WhenAll(taskA, taskB);
            
            avatarA = SKBitmap.Decode(taskA.Result);
            avatarB = SKBitmap.Decode(taskB.Result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[斗蛐蛐] 头像下载失败: {ex.Message}");
        }

        // ==========================================
        // 2. 将两个头像拼接成一张图 (左A右B) 喂给 AI
        // ==========================================
        string stitchedBase64 = "";
        using (var stitchedBitmap = new SKBitmap(1280, 640))
        using (var canvas = new SKCanvas(stitchedBitmap))
        {
            canvas.Clear(SKColors.Gray); // 兜底底色
            
            if (avatarA != null) canvas.DrawBitmap(avatarA, new SKRect(0, 0, 640, 640));
            if (avatarB != null) canvas.DrawBitmap(avatarB, new SKRect(640, 0, 1280, 640));

            using var img = SKImage.FromBitmap(stitchedBitmap);
            // 压缩质量设为 80 即可，AI 视觉不需要无损，能大幅提升传输速度
            using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80); 
            stitchedBase64 = $"data:image/jpeg;base64,{Convert.ToBase64String(data.ToArray())}";
        }

        // ==========================================
        // 3. 携带拼接好的单张图片请求 AI 战斗解说
        // ==========================================
        string battleLog = await GetAiBattleLogAsync(stitchedBase64, nameA, nameB);

        Console.WriteLine($"AI 战斗解说: {battleLog}");
        if (battleLog.StartsWith("❌"))
        {
            return battleLog;
        }

        // ==========================================
        // 4. 渲染最终街机格斗风结算图
        // ==========================================
        return DrawArcadeBattleImage(nameA, nameB, avatarA, avatarB, battleLog);
    }

    private static async Task<string> GetAiBattleLogAsync(string stitchedImageBase64, string nameA, string nameB)
    {
        // 专门为“拼接图”优化过的 Prompt
        string systemPrompt = $@"# Role: 街机格斗搞怪解说员
## 你的任务:
你现在是一场“赛博电子斗蛐蛐”的解说员。用户会给你一张拼接好的图片，图片**左半部分**是【决斗者A({nameA})】的头像，图片**右半部分**是【决斗者B({nameB})】的头像。
你需要仔细观察左右两边的视觉特征（人物、动物、物品、颜色、表情等），脑补一场极其抽象、无厘头、沙雕的格斗过程。

## 决斗规则与格式限制 (严格遵守):
1. **战斗过程**：双方交替攻击，最多不超过 8 个回合。每一回合必须描述清楚是谁用了什么基于头像特征的奇葩技能/武器，攻击了对方的什么部位，对方因此有什么反应。
2. **决斗结果**：最后必须给出明确的胜负判定。
3. **输出格式**：不要有多余的废话，严格按照以下格式输出：

回合1: {nameA}使用了[技能描述]攻击了{nameB}的[部位]...
回合2: {nameB}反击，使用...
回合3: ...
...
【决斗结果】: {nameA}/{nameB} 获得了最终胜利！(简述击倒原因)";

        var payload = new
        {
            model = ModelName,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new 
                { 
                    role = "user", 
                    content = new object[]
                    {
                        new { type = "text", text = $"请看这张拼接图，左边是决斗者A({nameA})，右边是决斗者B({nameB})。Fight！" },
                        new { type = "image_url", image_url = new { url = stitchedImageBase64 } }
                    }
                }
            },
            max_tokens = 500
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("Authorization", $"Bearer {ApiKey}");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return $"❌ 擂台断电了：HTTP {(int)response.StatusCode}";

            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "❌ 裁判睡着了。";
        }
        catch (Exception ex)
        {
            string realError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return $"❌ 决斗引发了时空乱流，比赛取消。({realError})";
        }
    }

    // ==========================================
    // 核心引擎：街机风动态渲染 (含颜色断层修复)
    // ==========================================
    private static string DrawArcadeBattleImage(string nameA, string nameB, SKBitmap? avatarA, SKBitmap? avatarB, string battleLog)
    {
        var normalTypeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        var textPaint = new SKPaint { Color = SKColors.White, Typeface = normalTypeface, TextSize = 22, IsAntialias = true };
        
        float maxTextWidth = 700;
        
        List<(string text, SKColor color)> displayLines = new List<(string, SKColor)>(); 
        string[] paragraphs = battleLog.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
        
        foreach (var para in paragraphs)
        {
            SKColor paraColor = SKColors.White;
            if (para.StartsWith("回合")) paraColor = SKColor.Parse("#7BED9F"); // 荧光绿
            else if (para.Contains("决斗结果") || para.Contains("最终胜利")) paraColor = SKColor.Parse("#FFA502"); // 亮橙色

            string currentLine = "";
            for (int i = 0; i < para.Length; i++)
            {
                string testLine = currentLine + para[i];
                if (textPaint.MeasureText(testLine) > maxTextWidth)
                {
                    displayLines.Add((currentLine, paraColor));
                    currentLine = para[i].ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            if (!string.IsNullOrEmpty(currentLine)) displayLines.Add((currentLine, paraColor));
            
            displayLines.Add(("", SKColors.Transparent)); 
        }

        int width = 800;
        float headerHeight = 350; 
        float lineHeight = 34;
        float textBlockHeight = displayLines.Count * lineHeight;
        int height = (int)(headerHeight + textBlockHeight + 80);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var bgColor = SKColor.Parse("#1E1E28");
        canvas.Clear(bgColor);

        var typefaceBold = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        // ================== 顶部视觉区 (头像与 VS) ==================
        using var hpBg = new SKPaint { Color = SKColor.Parse("#333333"), Style = SKPaintStyle.Fill };
        using var hpA = new SKPaint { Color = SKColor.Parse("#FF4757"), Style = SKPaintStyle.Fill };
        using var hpB = new SKPaint { Color = SKColor.Parse("#1E90FF"), Style = SKPaintStyle.Fill };
        canvas.DrawRect(30, 30, 300, 20, hpBg);
        canvas.DrawRect(30, 30, 250, 20, hpA); 
        canvas.DrawRect(width - 330, 30, 300, 20, hpBg);
        canvas.DrawRect(width - 330, 30, 280, 20, hpB); 

        var rectA = new SKRect(60, 70, 240, 250);
        var rectB = new SKRect(width - 240, 70, width - 60, 250);

        if (avatarA != null) canvas.DrawBitmap(avatarA, rectA);
        else canvas.DrawRect(rectA, new SKPaint { Color = SKColors.Gray });
        canvas.DrawRect(rectA, new SKPaint { Color = SKColor.Parse("#FF4757"), Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true });

        if (avatarB != null) canvas.DrawBitmap(avatarB, rectB);
        else canvas.DrawRect(rectB, new SKPaint { Color = SKColors.Gray });
        canvas.DrawRect(rectB, new SKPaint { Color = SKColor.Parse("#1E90FF"), Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true });

        using var vsPaint = new SKPaint { Color = SKColor.Parse("#FFA502"), Typeface = typefaceBold, TextSize = 80, TextAlign = SKTextAlign.Center, IsAntialias = true, FakeBoldText = true };
        canvas.DrawText("VS", width / 2f, 180, vsPaint);

        canvas.DrawText(nameA, 150, 290, new SKPaint { Color = SKColors.White, Typeface = typefaceBold, TextSize = 24, TextAlign = SKTextAlign.Center, IsAntialias = true });
        canvas.DrawText(nameB, width - 150, 290, new SKPaint { Color = SKColors.White, Typeface = typefaceBold, TextSize = 24, TextAlign = SKTextAlign.Center, IsAntialias = true });

        canvas.DrawLine(30, 330, width - 30, 330, new SKPaint { Color = SKColors.Gray, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash(new float[] { 10, 10 }, 0) });

        // ================== 下方文字日志区 ==================
        float currentY = 380;
        
        foreach (var item in displayLines)
        {
            if (string.IsNullOrEmpty(item.text)) currentY += 5;
            else
            {
                using var paint = new SKPaint { Color = item.color, Typeface = normalTypeface, TextSize = 22, IsAntialias = true };
                string drawLine = item.text.Replace("**", ""); 
                canvas.DrawText(drawLine, 50, currentY, paint);
                currentY += lineHeight;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}