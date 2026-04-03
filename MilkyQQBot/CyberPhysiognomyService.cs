using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SkiaSharp;

namespace MilkyQQBot;

public static class CyberPhysiognomyService
{ 
    private static readonly HttpClient _httpClient = new();

    private static string ApiUrl => AppConfig.Current.Ai.Physiognomy.ApiUrl;
    private static string ApiKey => AppConfig.Current.Ai.Physiognomy.ApiKey;
    private static string ModelName => AppConfig.Current.Ai.Physiognomy.Model;

    public static async Task<string> GenerateReportImageBase64Async(string targetQqId, string nickname)
    {
        string avatarUrl = $"http://q1.qlogo.cn/g?b=qq&nk={targetQqId}&s=640";

        // 1. 获取 GPT 的毒舌点评
        string reportText = await GetAiAnalysisAsync(avatarUrl);
        if (reportText.StartsWith("❌"))
        {
            return reportText;
        }

        // 2. 下载头像图片用于绘图
        SKBitmap? avatarImage = null;
        try
        {
            var imgBytes = await _httpClient.GetByteArrayAsync(avatarUrl);
            avatarImage = SKBitmap.Decode(imgBytes);
        }
        catch { }

        Console.WriteLine($"[AI头像鉴定返回]：\n{reportText}");
        // 3. 开始沙雕发疯风动态图像渲染
        return DrawUnhingedReport(targetQqId, nickname, avatarImage, reportText);
    }

    private static async Task<string> GetAiAnalysisAsync(string avatarUrl)
    {
        string systemPrompt = @"# Role: 奈奈川的赛博相面馆 (毒舌成分鉴定专家)
## 你的任务:
你现在是一个极具洞察力、极其毒舌、精通ACG文化和网络抽象迷因的“赛博相面大师”。用户会给你发一张图片（这是他的QQ头像），你需要对他进行无情的“成分鉴定”和“性格侧写”。

## 鉴定流程与输出要求 (严格遵守):
1. **火眼金睛 (识别)**：第一眼指出这个头像到底是个什么东西。如果认出是某部动漫、游戏的角色，或者是某个名人、知名梗图，**必须精准报出作品名和角色名**，并顺便吐槽一下这个角色本身。**如果没认出来或者不确定，则不必指出，避免出糗**。
2. **精神侧写 (吐槽)**：根据他选这个头像的心理，进行刻板印象嘲讽。比如：萌妹头像大概率是死宅，黑白抑郁风大概率是初二病，抽象图大概率精神堪忧。
3. **成分雷达图**：在结尾，必须列出一个百分比成分表。例如：“成分：40%的妄想症 + 30%的睡眠不足 + 30%的纯粹逆天”。

## 语气与禁忌:
- 极其刻薄、戏谑、一针见血。不要有任何AI的礼貌和温和！
- 字数控制在 250 字以内，直接输出吐槽内容。绝对不要说“这张图片是”。";

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
                        new { type = "text", text = "大师，给我狠狠地鉴定一下这个人的头像！" },
                        new { type = "image_url", image_url = new { url = avatarUrl } }
                    }
                }
            },
            max_tokens = 350
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("Authorization", $"Bearer {ApiKey}");
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return $"❌ 赛博诊所挂号失败：HTTP {(int)response.StatusCode}";

            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "❌ 大师闭口不言。";
        }
        catch (Exception ex)
        {
            return $"❌ 大师看你的头像看得眼睛流血，拒绝了鉴定。({ex.Message})";
        }
    }

    // ==========================================
    // 核心引擎：沙雕发疯风格动态渲染
    // ==========================================
    private static string DrawUnhingedReport(string qqId, string nickname, SKBitmap? avatar, string text)
    {
        // 1. 解析成分数据
        var components = new List<(float percent, string label)>();
        float totalPercent = 0;
        
        var matches = Regex.Matches(text, @"(\d{1,3}(?:\.\d+)?)%\s*(?:的)?\s*([^+%,\n\r。]+)");
        foreach (Match m in matches)
        {
            if (float.TryParse(m.Groups[1].Value, out float p))
            {
                components.Add((p, m.Groups[2].Value.Trim()));
                totalPercent += p;
            }
        }
        if (totalPercent <= 0) totalPercent = 1;

        // 2. 截断重复文本
        string cleanText = Regex.Replace(text, @"\**成分(?:雷达图|透视|分析)？\**[:：\n\r]*[\s\S]*", "", RegexOptions.IgnoreCase).Trim();
        int compIndex = cleanText.LastIndexOf("成分：");
        if (compIndex > 0) cleanText = cleanText.Substring(0, compIndex).Trim();

        // 3. 动态高度计算
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        var normalTypeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        var textPaint = new SKPaint { Color = SKColor.Parse("#2C3E50"), Typeface = normalTypeface, TextSize = 22, IsAntialias = true };
        
        float maxTextWidth = 700;
        List<string> displayLines = new List<string>();
        string[] paragraphs = cleanText.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
        
        foreach (var para in paragraphs)
        {
            string currentLine = "";
            for (int i = 0; i < para.Length; i++)
            {
                string testLine = currentLine + para[i];
                if (textPaint.MeasureText(testLine) > maxTextWidth)
                {
                    displayLines.Add(currentLine);
                    currentLine = para[i].ToString();
                }
                else
                {
                    currentLine = testLine;
                }
            }
            if (!string.IsNullOrEmpty(currentLine)) displayLines.Add(currentLine);
            displayLines.Add(""); 
        }

        int width = 800;
        float headerHeight = 480;
        float lineHeight = 34;
        float textBlockHeight = displayLines.Count * lineHeight;
        float pieChartHeight = components.Count > 0 ? 300 : 0;
        int height = (int)(headerHeight + textBlockHeight + pieChartHeight + 150); // 底部留白加大，用来画狂草签名

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // ==========================================
        // 沙雕病院配色 (Light/Crazy Mode)
        // ==========================================
        var paperColor = SKColor.Parse("#FFFDF8");      // 苍白病历纸
        var gridColor = SKColor.Parse("#D2DAE2").WithAlpha(100); // 医院蓝灰网格线
        var inkColor = SKColor.Parse("#2F3542");        // 钢笔黑
        var stampColor = SKColor.Parse("#FF4757");      // 刺眼的红印章
        var highlighterColor = SKColor.Parse("#FFEAA7"); // 马克笔亮黄

        canvas.Clear(paperColor);

        // 绘制满屏的病历网格底纹 (精神病理单专属)
        using var gridPaint = new SKPaint { Color = gridColor, StrokeWidth = 1.5f, IsAntialias = true };
        for (int x = 0; x < width; x += 30) canvas.DrawLine(x, 0, x, height, gridPaint);
        for (int y = 0; y < height; y += 30) canvas.DrawLine(0, y, width, y, gridPaint);

        // ==========================================
        // 灵魂巨型水印：“重度患者”
        // ==========================================
        canvas.Save();
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(-30);
        canvas.DrawText("重 度 患 者", 0, 0, new SKPaint { 
            Color = stampColor.WithAlpha(25), // 极度透明的红色
            Typeface = typeface, 
            TextSize = 130, 
            TextAlign = SKTextAlign.Center, 
            IsAntialias = true, 
            FakeBoldText = true 
        });
        canvas.Restore();

        // 外边框涂鸦
        using var borderPaint = new SKPaint { Color = inkColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        canvas.DrawRect(15, 15, width - 30, height - 30, borderPaint);
        canvas.DrawRect(22, 22, width - 44, height - 44, new SKPaint { Color = inkColor.WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true });

        // 顶部大标题
        canvas.DrawText("阿卡姆(赛博)疯人院 - 查房记录单", width / 2f, 80, new SKPaint { Color = inkColor, Typeface = typeface, TextSize = 40, TextAlign = SKTextAlign.Center, IsAntialias = true, FakeBoldText = true });
        
        using var linePaint = new SKPaint { Color = inkColor, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(80, 110, 720, 110, linePaint);
        canvas.DrawLine(80, 118, 720, 116, new SKPaint { Color = inkColor.WithAlpha(120), StrokeWidth = 2, IsAntialias = true });

        // 绘制头像 
        var avatarRect = new SKRect(50, 150, 250, 350);
        
        if (avatar != null) canvas.DrawBitmap(avatar, avatarRect);
        else
        {
            canvas.DrawRect(avatarRect, new SKPaint { Color = SKColor.Parse("#EAEAEA") });
            canvas.DrawText("患者出逃", 150, 260, new SKPaint { Color = SKColors.Gray, TextSize = 24, TextAlign = SKTextAlign.Center, Typeface = typeface });
        }
        
        // 给头像加一个粗糙的黑框和红色警告框
        canvas.DrawRect(avatarRect, new SKPaint { Color = inkColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true });
        canvas.DrawRect(avatarRect.Left - 6, avatarRect.Top - 6, avatarRect.Width + 12, avatarRect.Height + 12, new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash(new float[] { 10, 10 }, 0) });

        // 头像上的微型印章“放弃治疗”
        canvas.Save();
        canvas.Translate(150, 320);
        canvas.RotateDegrees(20);
        canvas.DrawRoundRect(new SKRect(-45, -15, 45, 15), 3, 3, new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true });
        canvas.DrawText("放弃治疗", 0, 6, new SKPaint { Color = stampColor, Typeface = typeface, TextSize = 18, TextAlign = SKTextAlign.Center, IsAntialias = true, FakeBoldText = true });
        canvas.Restore();

        // 极其敷衍的黄色胶带贴在照片顶端
        canvas.Save();
        canvas.RotateDegrees(-4, 150, 140);
        canvas.DrawRect(90, 135, 120, 25, new SKPaint { Color = SKColor.Parse("#FFD32A").WithAlpha(200), Style = SKPaintStyle.Fill, IsAntialias = true });
        canvas.DrawRect(90, 135, 120, 25, new SKPaint { Color = inkColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1 });
        canvas.Restore();

        // 右侧档案信息 (荧光笔涂鸦背景)
        float infoX = 290;
        canvas.DrawRect(infoX - 5, 160, 150, 35, new SKPaint { Color = highlighterColor, Style = SKPaintStyle.Fill });
        canvas.DrawText("收容物档案:", infoX, 190, new SKPaint { Color = inkColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });
        canvas.DrawText($"狂号：{nickname}", infoX, 240, new SKPaint { Color = inkColor, Typeface = normalTypeface, TextSize = 24, IsAntialias = true });
        canvas.DrawText($"编号：{qqId}", infoX, 290, new SKPaint { Color = inkColor, Typeface = normalTypeface, TextSize = 24, IsAntialias = true });
        
        // 撕裂感虚线
        using var dashPaint = new SKPaint { Color = inkColor.WithAlpha(100), Style = SKPaintStyle.Stroke, StrokeWidth = 2, PathEffect = SKPathEffect.CreateDash(new float[] { 15, 10 }, 0) };
        canvas.DrawLine(30, 390, 770, 390, dashPaint);

        // 荧光笔涂鸦背景 2
        canvas.DrawRect(45, 410, 180, 35, new SKPaint { Color = highlighterColor, Style = SKPaintStyle.Fill });
        canvas.DrawText("临床精神侧写:", 50, 440, new SKPaint { Color = inkColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });
        
        float currentY = headerHeight;
        foreach (var line in displayLines)
        {
            if (string.IsNullOrEmpty(line)) currentY += 10;
            else
            {
                string drawLine = line.Replace("**", ""); 
                canvas.DrawText(drawLine, 50, currentY, textPaint);
                currentY += lineHeight;
            }
        }

        // ==========================================
        // 绘制成分饼状图 (高饱和度毒蘑菇配色)
        // ==========================================
        if (components.Count > 0)
        {
            currentY += 20;
            canvas.DrawRect(45, currentY - 30, 180, 35, new SKPaint { Color = highlighterColor, Style = SKPaintStyle.Fill });
            canvas.DrawText("脑部切片透视:", 50, currentY, new SKPaint { Color = inkColor, Typeface = typeface, TextSize = 26, IsAntialias = true, FakeBoldText = true });

            float pieCenterY = currentY + 130;
            float pieCenterX = 180;
            float pieRadius = 100;
            var pieRect = new SKRect(pieCenterX - pieRadius, pieCenterY - pieRadius, pieCenterX + pieRadius, pieCenterY + pieRadius);
            
            float startAngle = -90; 
            
            // 毒蘑菇配色 (亮黄、毒绿、警告红等高对比度色彩)
            SKColor[] palette = { 
                SKColor.Parse("#FF3838"), // 警告红
                SKColor.Parse("#32FF7E"), // 辐射绿
                SKColor.Parse("#FFF200"), // 亮瞎黄
                SKColor.Parse("#18DCFF"), // 精神蓝
                SKColor.Parse("#CD84F1"), // 迷幻紫
                SKColor.Parse("#FF9F1A")  // 警戒橙
            };

            using var pieStroke = new SKPaint { Color = inkColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
            
            float legendX = 330;
            float legendY = currentY + 40;

            for (int i = 0; i < components.Count; i++)
            {
                var comp = components[i];
                float sweepAngle = (comp.percent / totalPercent) * 360f; 

                using var fillPaint = new SKPaint { Color = palette[i % palette.Length], Style = SKPaintStyle.Fill, IsAntialias = true };

                canvas.DrawArc(pieRect, startAngle, sweepAngle, true, fillPaint);
                canvas.DrawArc(pieRect, startAngle, sweepAngle, true, pieStroke); // 画出粗犷的黑边

                startAngle += sweepAngle;

                canvas.DrawRect(legendX, legendY + i * 35, 20, 20, fillPaint);
                canvas.DrawRect(legendX, legendY + i * 35, 20, 20, pieStroke);
                
                string displayLabel = comp.label.Length > 12 ? comp.label.Substring(0, 11) + "..." : comp.label;
                canvas.DrawText($"{displayLabel} ({comp.percent}%)", legendX + 35, legendY + 16 + i * 35, textPaint);
            }
        }

        // ==========================================
        // 底部：主治医师疯狂签名 + 绝望印章
        // ==========================================
        float footerY = height - 90;
        
        // 1. 模拟医生画符一样的狂草签名
        canvas.DrawText("主治医师签字：", 50, footerY + 20, new SKPaint { Color = inkColor, Typeface = typeface, TextSize = 24, IsAntialias = true, FakeBoldText = true });
        using var signaturePath = new SKPath();
        signaturePath.MoveTo(210, footerY + 10);
        signaturePath.LineTo(230, footerY - 10);
        signaturePath.LineTo(240, footerY + 30);
        signaturePath.LineTo(260, footerY - 5);
        signaturePath.LineTo(280, footerY + 20);
        signaturePath.LineTo(320, footerY + 5);
        canvas.DrawPath(signaturePath, new SKPaint { Color = SKColor.Parse("#0984E3"), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true }); // 蓝色圆珠笔效果
        canvas.DrawText("奈奈川", 240, footerY + 45, new SKPaint { Color = inkColor.WithAlpha(100), TextSize = 16, Typeface = normalTypeface }); // 偷偷盖个小字防止真认不出来

        // 2. 右下角疯狂印章
        canvas.Save();
        canvas.Translate(620, footerY + 10); 
        canvas.RotateDegrees(-20); // 歪斜增加灵魂
        
        using var stampStroke = new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(-95, -35, 95, 35), 8, 8, stampStroke);
        canvas.DrawRoundRect(new SKRect(-88, -28, 88, 28), 3, 3, new SKPaint { Color = stampColor, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true });
        
        canvas.DrawText("建议重开", 0, 12, new SKPaint { Color = stampColor, Typeface = typeface, TextSize = 34, TextAlign = SKTextAlign.Center, IsAntialias = true, FakeBoldText = true });
        canvas.Restore();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}