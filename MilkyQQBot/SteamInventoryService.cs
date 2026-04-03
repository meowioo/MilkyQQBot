using System.Text.Json;
using SkiaSharp;

namespace MilkyQQBot;

public class CsgoItem
{
    public string Name { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string NameColor { get; set; } = "D2D2D2"; 
    public int RarityScore { get; set; } // 用于排序的稀有度分数
    public SKBitmap? Image { get; set; }
}

public class SteamInventoryResult
{
    public string PersonaName { get; set; } = "未知玩家";
    public string AvatarUrl { get; set; } = "";
    public SKBitmap? Avatar { get; set; }
    public List<CsgoItem> TopItems { get; set; } = new();
    public int TotalItemsCount { get; set; }
    public bool IsPrivate { get; set; } = false;
    public string ErrorMsg { get; set; } = "";
}

public static class SteamInventoryService
{
    private static readonly HttpClient _httpClient = new();
    private static string ApiKey => AppConfig.Current.Steam.ApiKey;

    static SteamInventoryService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    }

    public static async Task<SteamInventoryResult> GetInventoryAsync(string steamId)
    {
        var result = new SteamInventoryResult();
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

            // 2. 调取 CS2 (AppID: 730) 库存 (最多拉取 1000 件)
            string invUrl = $"https://steamcommunity.com/inventory/{steamId}/730/2?l=schinese&count=1000";
            var res = await _httpClient.GetAsync(invUrl);

            if (!res.IsSuccessStatusCode)
            {
                result.IsPrivate = true;
                result.ErrorMsg = "该玩家隐藏了库存，或者 Steam 接口正在抽风。";
                return result;
            }

            var invJson = await res.Content.ReadAsStringAsync();
            using var invDoc = JsonDocument.Parse(invJson);
            
            var root = invDoc.RootElement;
            if (root.TryGetProperty("success", out var succ) && succ.GetInt32() == 0)
            {
                result.IsPrivate = true;
                return result;
            }

            // 如果库存为空
            if (!root.TryGetProperty("assets", out var assetsArray) || !root.TryGetProperty("descriptions", out var descArray))
            {
                result.TotalItemsCount = 0;
                return result;
            }

            result.TotalItemsCount = assetsArray.GetArrayLength();

            // 解析 Descriptions 建立字典 (ClassId -> Item详情)
            var descDict = new Dictionary<string, CsgoItem>();
            foreach (var desc in descArray.EnumerateArray())
            {
                string classId = desc.GetProperty("classid").ToString();
                
                // 排除无法交易/无法出售的纯白嫖垃圾（如原生徽章、默认武器）
                bool tradable = desc.TryGetProperty("tradable", out var tr) && tr.GetInt32() == 1;
                bool marketable = desc.TryGetProperty("marketable", out var ma) && ma.GetInt32() == 1;
                
                if (!tradable && !marketable) continue;

                string nameColor = desc.TryGetProperty("name_color", out var nc) ? nc.GetString() ?? "D2D2D2" : "D2D2D2";
                
                // 赛博估值系统：将颜色代码转换为稀有度分数
                int rarityScore = nameColor.ToUpper() switch
                {
                    "FFD700" => 100, // 违禁 / 极其珍贵 (纯金)
                    "EB4B4B" => 90,  // 隐秘 (红)
                    "D32CE6" => 80,  // 保密 (粉)
                    "8847FF" => 70,  // 受限 (紫)
                    "4B69FF" => 60,  // 军规 (深蓝)
                    "5E98D9" => 50,  // 工业 (浅蓝)
                    "B0C3D9" => 40,  // 消费 (灰白)
                    _ => 10          // 默认垃圾
                };

                // 优先把名字里的“箱”、“胶囊”降权，避免九宫格全是箱子
                string name = desc.TryGetProperty("market_hash_name", out var mn) ? mn.GetString() ?? "" : "";
                if (name.Contains("Case") || name.Contains("箱") || name.Contains("Capsule") || name.Contains("胶囊"))
                {
                    rarityScore -= 30; 
                }

                if (!descDict.ContainsKey(classId))
                {
                    descDict[classId] = new CsgoItem
                    {
                        Name = name,
                        IconUrl = desc.TryGetProperty("icon_url", out var iu) ? iu.GetString() ?? "" : "",
                        NameColor = nameColor,
                        RarityScore = rarityScore
                    };
                }
            }

            // 匹配库存资产与详情，去重（同样的枪皮只展示一把）
            var uniqueItems = new Dictionary<string, CsgoItem>();
            foreach (var asset in assetsArray.EnumerateArray())
            {
                string classId = asset.GetProperty("classid").ToString();
                if (descDict.TryGetValue(classId, out var itemDesc) && !string.IsNullOrEmpty(itemDesc.IconUrl))
                {
                    if (!uniqueItems.ContainsKey(itemDesc.Name))
                    {
                        uniqueItems[itemDesc.Name] = itemDesc;
                    }
                }
            }

            // 取出稀有度最高的 9 件宝贝
            result.TopItems = uniqueItems.Values
                .OrderByDescending(x => x.RarityScore)
                .Take(9)
                .ToList();

            // 并发下载这 9 件宝贝的高清大图
            foreach (var item in result.TopItems)
            {
                string fullImgUrl = $"https://community.cloudflare.steamstatic.com/economy/image/{item.IconUrl}";
                downloadTasks.Add(Task.Run(async () => {
                    try { item.Image = SKBitmap.Decode(await _httpClient.GetByteArrayAsync(fullImgUrl)); } catch { }
                }));
            }

            await Task.WhenAll(downloadTasks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[库存获取失败] {ex.Message}");
            return null;
        }

        return result;
    }

    // ==========================================
    // 动态渲染：新拟态手绘风 "赛博展柜盲盒"
    // ==========================================
    public static string GenerateInventoryCardBase64(SteamInventoryResult result)
    {
        int width = 800;
        int headerHeight = 160;
        int itemSize = 220; // 九宫格单格尺寸
        int spacing = 20;   // 间距
        
        // 计算行数 (每行 3 个)
        int rows = (int)Math.Ceiling(Math.Max(1, result.TopItems.Count) / 3.0);
        int height = headerHeight + (rows * (itemSize + spacing)) + 40;

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        var bgColor = SKColor.Parse("#Fdfbf7");
        var strokeColor = SKColor.Parse("#2d3436");
        var shadowColor = SKColor.Parse("#e8e1d5");
        
        canvas.Clear(bgColor);
        var typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = 4, IsAntialias = true };
        using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };

        // 1. 画顶部头像和标题
        var avatarRect = new SKRect(40, 30, 130, 120);
        if (result.Avatar != null)
        {
            canvas.Save();
            var clipPath = new SKPath();
            clipPath.AddRoundRect(avatarRect, 12, 12);
            canvas.ClipPath(clipPath);
            canvas.DrawBitmap(result.Avatar, avatarRect);
            canvas.Restore();
        }
        canvas.DrawRoundRect(avatarRect, 12, 12, strokePaint);

        canvas.DrawText($"{result.PersonaName} 的秘密金库", 160, 75, new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 38, IsAntialias = true, FakeBoldText = true });
        
        if (result.IsPrivate)
        {
            canvas.DrawText("该玩家拉下了卷帘门，不让别人看他的库存！", 160, 115, new SKPaint { Color = SKColor.Parse("#ff7675"), Typeface = typeface, TextSize = 22, IsAntialias = true, FakeBoldText = true });
            using var img = SKImage.FromBitmap(bitmap);
            using var d = img.Encode(SKEncodedImageFormat.Png, 100);
            return $"base64://{Convert.ToBase64String(d.ToArray())}";
        }

        canvas.DrawText($"库中共计积灰饰品: {result.TotalItemsCount} 件 (已为您挑出最值钱的宝贝)", 160, 115, new SKPaint { Color = SKColor.Parse("#636e72"), Typeface = typeface, TextSize = 20, IsAntialias = true });
        canvas.DrawLine(40, 140, width - 40, 140, strokePaint);

        // 2. 绘制九宫格展柜
        if (result.TopItems.Count == 0)
        {
            canvas.DrawText("翻了半天，这人库存里全是不值钱的破铜烂铁...", width / 2f, headerHeight + 100, new SKPaint { Color = SKColor.Parse("#636e72"), Typeface = typeface, TextSize = 24, IsAntialias = true, TextAlign = SKTextAlign.Center });
        }
        else
        {
            for (int i = 0; i < result.TopItems.Count; i++)
            {
                var item = result.TopItems[i];
                int col = i % 3;
                int row = i / 3;

                float x = 40 + col * (itemSize + spacing + 10);
                float y = headerHeight + row * (itemSize + spacing);

                // 根据饰品稀有度，给盲盒画一个带颜色的炫酷外发光底座
                var rarityColor = SKColor.Parse($"#{item.NameColor}");
                
                // 盲盒阴影
                canvas.DrawRoundRect(x + 6, y + 6, itemSize, itemSize, 12, 12, new SKPaint { Color = shadowColor, Style = SKPaintStyle.Fill, IsAntialias = true });
                // 盲盒主体
                canvas.DrawRoundRect(x, y, itemSize, itemSize, 12, 12, fillPaint);
                // 盲盒边框 (用稀有度颜色加粗描边)
                canvas.DrawRoundRect(x, y, itemSize, itemSize, 12, 12, new SKPaint { Color = rarityColor, Style = SKPaintStyle.Stroke, StrokeWidth = 5, IsAntialias = true });

                // 绘制饰品图案 (居中)
                float imgW = 180;
                float imgH = 140;
                float imgX = x + (itemSize - imgW) / 2f;
                float imgY = y + 15;
                
                if (item.Image != null)
                {
                    // 图片保持比例缩放
                    var srcRect = new SKRect(0, 0, item.Image.Width, item.Image.Height);
                    var destRect = new SKRect(imgX, imgY, imgX + imgW, imgY + imgH);
                    canvas.DrawBitmap(item.Image, srcRect, destRect);
                }

                // 绘制饰品名称 (如果太长则分两行)
                var namePaint = new SKPaint { Color = strokeColor, Typeface = typeface, TextSize = 18, IsAntialias = true, FakeBoldText = true, TextAlign = SKTextAlign.Center };
                string cleanName = item.Name.Replace("★ ", ""); // 去掉匕首前面的星号防止乱码
                
                if (namePaint.MeasureText(cleanName) > itemSize - 20)
                {
                    string part1 = cleanName.Substring(0, cleanName.Length / 2);
                    string part2 = cleanName.Substring(cleanName.Length / 2);
                    canvas.DrawText(part1, x + itemSize / 2f, y + itemSize - 35, namePaint);
                    canvas.DrawText(part2, x + itemSize / 2f, y + itemSize - 15, namePaint);
                }
                else
                {
                    canvas.DrawText(cleanName, x + itemSize / 2f, y + itemSize - 20, namePaint);
                }
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return $"base64://{Convert.ToBase64String(data.ToArray())}";
    }
}