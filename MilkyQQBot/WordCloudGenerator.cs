using SkiaSharp;
using JiebaNet.Segmenter;

namespace MilkyQQBot;

public static class WordCloudGenerator
{
    // 初始化结巴分词器 (首次调用会有约1秒的词典加载延迟)
    private static readonly JiebaSegmenter _segmenter = new();
    
    // 停用词表：过滤掉聊天中没有实际意义的高频词汇
    private static readonly HashSet<string> _stopWords = new() 
    { 
        "什么", "怎么", "我们", "你们", "他们", "一个", "没有", "这个", "那个", "就是", 
        "还是", "可以", "如果", "虽然", "但是", "因为", "所以", "不过", "觉得", "知道", 
        "出来", "现在", "不是", "真的", "这么", "那么", "自己", "时候", "真的", "大家",
        "图片", "表情", "撤回", "一条", "消息"
    };

    public static async Task<string> GenerateAsync(List<string> messages, string title)
    {
        // 1. 拼接所有文本并进行分词
        var allText = string.Join(" ", messages);
        var words = _segmenter.Cut(allText);

        // 2. 词频统计与过滤
        var wordCounts = words
            .Where(w => w.Length > 1 && !_stopWords.Contains(w)) // 只保留长度>1的词，且不在停用词表中
            .GroupBy(w => w)
            .Select(g => new { Word = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(80) // 提取出现频率最高的 80 个词
            .ToList();

        if (wordCounts.Count == 0) return null;

        // 3. 初始化画板
        int width = 800;
        int height = 600;
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        
        // 护眼暗夜背景色，让词云色彩更突出
        canvas.Clear(SKColor.Parse("#1E272E")); 

        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) 
                       ?? SKTypeface.Default;

        // 画标题
        using var titlePaint = new SKPaint { Color = SKColors.White, Typeface = typeface, TextSize = 28, IsAntialias = true, FakeBoldText = true };
        canvas.DrawText(title, 30, 40, titlePaint);

        // 4. 阿基米德螺旋线布局算法 (核心逻辑)
        var placedRects = new List<SKRect>();
        var random = new Random();
        
        // 现代感马卡龙荧光配色
        var colors = new[] { "#FF9FF3", "#feca57", "#ff6b6b", "#48dbfb", "#1dd1a1", "#00d2d3", "#5f27cd", "#c8d6e5" }
            .Select(SKColor.Parse).ToList();

        float maxCount = wordCounts.First().Count;
        float minCount = wordCounts.Last().Count;

        foreach (var item in wordCounts)
        {
            // 归一化计算字体大小 (出现频率越高的词越大，最小 16px，最大 90px)
            float fontSize = 16f + ((item.Count - minCount) / (maxCount - minCount + 0.0001f)) * 74f;
            
            using var paint = new SKPaint 
            { 
                Typeface = typeface, 
                TextSize = fontSize, 
                IsAntialias = true, 
                Color = colors[random.Next(colors.Count)],
                FakeBoldText = true 
            };

            // 测量文字的物理边界大小
            var bounds = new SKRect();
            paint.MeasureText(item.Word, ref bounds);

            bool placed = false;
            float angle = 0;
            float spiralStep = 0.5f;

            // 螺旋向外寻找空位
            while (!placed && angle < 1000)
            {
                float radius = 3.0f * angle; // 圈与圈之间的间距
                float x = (width / 2f) + radius * (float)Math.Cos(angle);
                float y = (height / 2f) + radius * (float)Math.Sin(angle);

                // 根据中心点计算出当前文字如果放在这里的矩形框
                var rect = new SKRect(
                    x - bounds.Width / 2f, 
                    y - bounds.Height / 2f, 
                    x + bounds.Width / 2f, 
                    y + bounds.Height / 2f
                );

                // 边界检查：不能画到图片外面去
                if (rect.Left < 20 || rect.Right > width - 20 || rect.Top < 70 || rect.Bottom > height - 20)
                {
                    angle += spiralStep;
                    continue;
                }

                // 碰撞检测：检查是否和已经画上去的词重叠了
                bool intersect = false;
                var inflatedRect = rect;
                inflatedRect.Inflate(4, 4); // 给词语周围留 4px 的安全间距

                foreach (var placedRect in placedRects)
                {
                    if (inflatedRect.IntersectsWith(placedRect))
                    {
                        intersect = true;
                        break;
                    }
                }

                // 如果不重叠，就画上去！
                if (!intersect)
                {
                    // SkiaSharp DrawText 的 Y 坐标是基线，需要转换
                    canvas.DrawText(item.Word, rect.Left - bounds.Left, rect.Top - bounds.Top, paint);
                    placedRects.Add(rect);
                    placed = true;
                }

                angle += spiralStep;
            }
        }

        // 5. 导出 Base64
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}