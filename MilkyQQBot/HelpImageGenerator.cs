using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace MilkyQQBot; // 替换为你的命名空间

public class CommandInfo
{
    public string Command { get; set; } = "";
    public string Description { get; set; } = "";
    public string Hint { get; set; } = ""; 
    public float CalculatedHeight { get; set; } // 用于排版时临时存储高度
}

public static class HelpImageGenerator
{
    public static string GenerateBase64Image(string helpText)
    {
        var commands = ParseHelpText(helpText);

        // ==========================================
        // 1. 漫画风基础排版参数
        // ==========================================
        int width = 1400; // 采用 1400 的宽屏尺寸，完美容纳双列
        int padding = 50; // 边缘留白
        int cardMargin = 35; // 卡片之间的间距

        // 双列宽度计算：(总宽 - 左右边缘留白 - 中间留白) / 2
        float colWidth = (width - (padding * 3)) / 2f; 
        
        // 漫画波普风配色板
        var bgColor = SKColor.Parse("#FDFBF7");        // 略微发黄的漫画纸底色
        var cardColor = SKColors.White;                // 卡片底色
        var strokeColor = SKColor.Parse("#1A1A1A");    // 粗黑线描边色
        var shadowColor = SKColor.Parse("#D1CCC5");    // 漫画风硬阴影
        
        var descColor = SKColor.Parse("#333333");      // 正文深灰
        var hintBgColor = SKColor.Parse("#FFF3CD");    // 提示框亮黄色
        var hintStrokeColor = SKColor.Parse("#FFC107"); // 提示框边框色

        // 糖果色指令徽章池 (类似漫画里的多彩网点)
        SKColor[] badgeColors = {
            SKColor.Parse("#FFB3BA"), // 元气粉
            SKColor.Parse("#BAE1FF"), // 晴空蓝
            SKColor.Parse("#BAFFC9"), // 薄荷绿
            SKColor.Parse("#FFFFBA"), // 柠檬黄
            SKColor.Parse("#E8BAFF"), // 罗兰紫
            SKColor.Parse("#FFDFBA")  // 活力橙
        };

        // 字体设定
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        var boldTypeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;

        var titlePaint = new SKPaint { Color = strokeColor, Typeface = boldTypeface, TextSize = 48, IsAntialias = true, FakeBoldText = true };
        var cmdPaint = new SKPaint { Color = strokeColor, Typeface = boldTypeface, TextSize = 26, IsAntialias = true, FakeBoldText = true };
        var descPaint = new SKPaint { Color = descColor, Typeface = typeface, TextSize = 24, IsAntialias = true };
        var hintPaint = new SKPaint { Color = SKColor.Parse("#A36A00"), Typeface = boldTypeface, TextSize = 22, IsAntialias = true };
        
        var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true }; // 核心：漫画感粗描边

        // ==========================================
        // 2. 动态高度预计算 (精准不溢出)
        // ==========================================
        float cardInnerPadding = 30; // 卡片左右内边距
        float cardContentW = colWidth - (cardInnerPadding * 2); // 文本最大可用宽度

        foreach (var cmd in commands)
        {
            float h = 30; // 顶部留白
            h += 45; // 指令徽章高度
            h += 15; // 徽章与描述的间距

            var descLines = WrapText(cmd.Description, descPaint, cardContentW);
            h += descLines.Count * 36; // 描述文本高度 (行高36)

            if (!string.IsNullOrEmpty(cmd.Hint))
            {
                h += 20; // 提示框与上文的间距
                // 【核心修复】提示框的内部文本宽度必须比提示框还要小
                var hintLines = WrapText(cmd.Hint, hintPaint, cardContentW - 40); 
                h += hintLines.Count * 32 + 30; // 文本高度 + 提示框上下内边距
            }

            h += 30; // 底部留白
            cmd.CalculatedHeight = h;
        }

        // ==========================================
        // 3. 瀑布流双列布局算法
        // ==========================================
        float leftY = padding + 120;  // 左列起始Y坐标
        float rightY = padding + 120; // 右列起始Y坐标

        using var bitmapForMeasure = new SKBitmap(1, 1); // 仅用于撑开最终高度

        foreach (var cmd in commands)
        {
            if (leftY <= rightY) leftY += cmd.CalculatedHeight + cardMargin;
            else rightY += cmd.CalculatedHeight + cardMargin;
        }

        int totalHeight = (int)Math.Max(leftY, rightY) + padding;

        // ==========================================
        // 4. 正式绘制
        // ==========================================
        using var bitmap = new SKBitmap(width, totalHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(bgColor);

        // A. 绘制大标题
        string titleText = " 奈奈川の指令手册 ";
        var titleBounds = new SKRect();
        titlePaint.MeasureText(titleText, ref titleBounds);
        canvas.DrawText(titleText, (width - titleBounds.Width) / 2f, padding + 50, titlePaint);
        
        // 标题下方的漫画风波浪/虚线
        using var titleLinePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 15, 10 }, 0) };
        canvas.DrawLine(width / 2f - 250, padding + 75, width / 2f + 250, padding + 75, titleLinePaint);

        // B. 重新运行一遍布局逻辑，并把卡片画上去
        leftY = padding + 120;  
        rightY = padding + 120; 
        int colorIndex = 0;

        foreach (var cmd in commands)
        {
            float currentX = 0;
            float currentY = 0;

            // 决定这张卡片放左边还是右边
            if (leftY <= rightY)
            {
                currentX = padding;
                currentY = leftY;
                leftY += cmd.CalculatedHeight + cardMargin;
            }
            else
            {
                currentX = padding * 2 + colWidth;
                currentY = rightY;
                rightY += cmd.CalculatedHeight + cardMargin;
            }

            float cardW = colWidth;
            float cardH = cmd.CalculatedHeight;

            // 1) 漫画风硬阴影 (向右下方偏移 8px)
            using var shadowPaintObj = new SKPaint { Color = shadowColor, IsAntialias = true };
            canvas.DrawRoundRect(currentX + 8, currentY + 8, cardW, cardH, 16, 16, shadowPaintObj);

            // 2) 卡片主板与黑框粗描边
            using var cardPaintObj = new SKPaint { Color = cardColor, IsAntialias = true };
            canvas.DrawRoundRect(currentX, currentY, cardW, cardH, 16, 16, cardPaintObj);
            canvas.DrawRoundRect(currentX, currentY, cardW, cardH, 16, 16, strokePaint);

            float drawY = currentY + 30;

            // 3) 糖果色指令徽章
            var cmdBounds = new SKRect();
            cmdPaint.MeasureText(cmd.Command, ref cmdBounds);
            float badgeW = cmdBounds.Width + 28;
            using var badgePaint = new SKPaint { Color = badgeColors[colorIndex % badgeColors.Length], IsAntialias = true };
            
            canvas.DrawRoundRect(currentX + cardInnerPadding, drawY, badgeW, 45, 8, 8, badgePaint);
            canvas.DrawRoundRect(currentX + cardInnerPadding, drawY, badgeW, 45, 8, 8, strokePaint); // 徽章也要黑边
            canvas.DrawText(cmd.Command, currentX + cardInnerPadding + 14, drawY + 32, cmdPaint);
            
            colorIndex++;
            drawY += 60;

            // 4) 描述文本
            var descLines = WrapText(cmd.Description, descPaint, cardContentW);
            foreach (var line in descLines)
            {
                drawY += 32; // 行高
                canvas.DrawText(line, currentX + cardInnerPadding, drawY, descPaint);
            }

            // 5) 💡专属提示框 (修复了溢出问题，内层文本精打细算)
            if (!string.IsNullOrEmpty(cmd.Hint))
            {
                drawY += 20;
                var hintLines = WrapText(cmd.Hint, hintPaint, cardContentW - 40); // 左右各留 20px 边距
                float hintBoxH = hintLines.Count * 32 + 30;

                // 提示框背景与描边
                using var hintBoxBg = new SKPaint { Color = hintBgColor, IsAntialias = true };
                using var hintBoxStroke = new SKPaint { Color = hintStrokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 10, 8 }, 0) }; // 虚线边框更俏皮
                
                canvas.DrawRoundRect(currentX + cardInnerPadding, drawY, cardContentW, hintBoxH, 12, 12, hintBoxBg);
                canvas.DrawRoundRect(currentX + cardInnerPadding, drawY, cardContentW, hintBoxH, 12, 12, hintBoxStroke);

                float hintTextY = drawY + 34; // 文本首行Y坐标
                foreach (var line in hintLines)
                {
                    canvas.DrawText(line, currentX + cardInnerPadding + 20, hintTextY, hintPaint);
                    hintTextY += 32;
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }

    // 解析器保持不变
    private static List<CommandInfo> ParseHelpText(string text)
    {
        var commands = new List<CommandInfo>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        CommandInfo currentCmd = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("当前可用指令列表")) continue; 
            if (line.TrimStart().StartsWith("！") && currentCmd != null)
            {
                currentCmd.Hint = line.Trim();
                continue;
            }

            var parts = line.Split(new[] { '-' }, 2);
            if (parts.Length == 2)
            {
                currentCmd = new CommandInfo
                {
                    Command = parts[0].Trim(),
                    Description = parts[1].Trim()
                };
                commands.Add(currentCmd);
            }
        }
        return commands;
    }

    // 中英文混排绝对不会切坏单词的换行器
    private static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        int start = 0;
        while (start < text.Length)
        {
            int length = 1;
            while (start + length <= text.Length && paint.MeasureText(text.Substring(start, length)) <= maxWidth)
            {
                length++;
            }
            length--; 
            if (length <= 0) length = 1; 

            lines.Add(text.Substring(start, length));
            start += length;
        }
        return lines;
    }
}