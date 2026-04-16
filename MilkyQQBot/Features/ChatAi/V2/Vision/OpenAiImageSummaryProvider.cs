using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ImageMagick;

namespace MilkyQQBot.Features.ChatAi.V2.Vision;

/// <summary>
/// 基于 OpenAI 兼容接口的图片摘要提供器。
/// 支持：
/// 1. 普通图片直接传 URL
/// 2. GIF 动图自动抽取一帧后再送给视觉模型
/// 3. 全流程 45 秒超时保护，避免任务一直卡在 running
/// </summary>
public sealed class OpenAiImageSummaryProvider : IImageSummaryProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiImageSummaryProvider(
        HttpClient httpClient,
        string apiUrl,
        string apiKey,
        string model)
    {
        _httpClient = httpClient;
        _apiUrl = apiUrl;
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string?> GenerateSummaryAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        // 总超时：45 秒
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
        var ct = timeoutCts.Token;

        // 构造真正送给视觉模型的图像输入：
        // - 普通图：直接原 URL
        // - GIF：抽一帧转成 JPEG，再转 data URL
        string visionInputUrl = await BuildVisionInputUrlAsync(imageUrl, ct);

        var requestBody = new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "你是一个群聊图片摘要器。请只输出一句很短的中文摘要，不要解释，不要标点，不要超过30个字。优先判断类型，如：聊天截图、梗图、表情包、游戏截图、宠物照片、风景照、商品图、代码截图、文档截图。然后尝试具体描述，例如：两个人在打斗、猫咪表情包、王者战绩图。"
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "请给这张图生成一句很短的中文摘要"
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = visionInputUrl
                            }
                        }
                    }
                }
            },
            temperature = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        string responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"图片摘要接口调用失败: {(int)response.StatusCode} {responseText}");
        }

        using var doc = JsonDocument.Parse(responseText);

        string? content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return NormalizeSummary(content);
    }

    /// <summary>
    /// 构造送给视觉模型的图片输入。
    /// 如果是 GIF，则抽取中间帧并转为 data URL。
    /// 否则直接返回原图 URL。
    /// </summary>
    private async Task<string> BuildVisionInputUrlAsync(string imageUrl, CancellationToken cancellationToken)
    {
        // 先做 URL 层面的粗判断
        bool maybeGif = LooksLikeGifUrl(imageUrl);

        // 如果 URL 看不出来，再尝试通过 content-type 判断
        if (!maybeGif)
        {
            string? contentType = await TryGetContentTypeAsync(imageUrl, cancellationToken);
            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.StartsWith("image/gif", StringComparison.OrdinalIgnoreCase))
            {
                maybeGif = true;
            }
        }

        // 普通图片直接走原 URL
        if (!maybeGif)
        {
            return imageUrl;
        }

        // GIF：下载二进制后抽一帧
        byte[] bytes = await DownloadImageBytesAsync(imageUrl, cancellationToken);

        // 再用文件头兜底确认一次
        if (!LooksLikeGifBytes(bytes))
        {
            return imageUrl;
        }

        byte[] jpegBytes = ExtractRepresentativeGifFrameAsJpeg(bytes);
        string base64 = Convert.ToBase64String(jpegBytes);

        // 直接作为 data URL 发给视觉模型
        return $"data:image/jpeg;base64,{base64}";
    }

    /// <summary>
    /// 下载图片字节。
    /// 这里会受上层 45 秒总超时约束。
    /// </summary>
    private async Task<byte[]> DownloadImageBytesAsync(string imageUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// 从 GIF 中抽取一个代表帧，并转成 JPEG。
    /// 这里取“中间帧”，通常比第一帧更能代表动图内容。
    /// </summary>
    private static byte[] ExtractRepresentativeGifFrameAsJpeg(byte[] gifBytes)
    {
        using var collection = new MagickImageCollection(gifBytes);

        if (collection.Count == 0)
            throw new Exception("GIF 中没有可用帧");

        // 先做 Coalesce，确保帧内容完整
        collection.Coalesce();

        int frameIndex = collection.Count / 2;

        using var frame = (MagickImage)collection[frameIndex].Clone();

        // 限制一下尺寸，避免 data URL 过大
        if (frame.Width > 1024 || frame.Height > 1024)
        {
            frame.Resize(new MagickGeometry(1024, 1024)
            {
                IgnoreAspectRatio = false
            });
        }

        frame.Format = MagickFormat.Jpeg;
        frame.Quality = 85;

        return frame.ToByteArray();
    }

    /// <summary>
    /// 通过 URL 粗略判断是否可能是 GIF。
    /// </summary>
    private static bool LooksLikeGifUrl(string imageUrl)
    {
        string lower = imageUrl.ToLowerInvariant();

        return lower.Contains(".gif") ||
               Regex.IsMatch(lower, @"(?:\?|&)(?:format|type|ext)=gif(?:&|$)");
    }

    /// <summary>
    /// 通过文件头判断是否是 GIF。
    /// 常见 GIF 文件头是 GIF87a / GIF89a。
    /// </summary>
    private static bool LooksLikeGifBytes(byte[] bytes)
    {
        if (bytes.Length < 6)
            return false;

        string header = Encoding.ASCII.GetString(bytes, 0, 6);
        return header == "GIF87a" || header == "GIF89a";
    }

    /// <summary>
    /// 尝试读取 content-type。
    /// 先用 HEAD，失败后再回退到 GET 的响应头。
    /// </summary>
    private async Task<string?> TryGetContentTypeAsync(string imageUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, imageUrl);
            using var headResponse = await _httpClient.SendAsync(
                headRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (headResponse.Content.Headers.ContentType?.MediaType is string headType &&
                !string.IsNullOrWhiteSpace(headType))
            {
                return headType;
            }
        }
        catch
        {
            // 某些图床不支持 HEAD，继续走 GET 兜底
        }

        try
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            using var getResponse = await _httpClient.SendAsync(
                getRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return getResponse.Content.Headers.ContentType?.MediaType;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把模型输出规范成适合写回数据库的短摘要。
    /// </summary>
    private static string? NormalizeSummary(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        string text = content.Trim();

        // 去掉换行和多余空白
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // 去掉句末标点
        text = text.Trim('。', '，', '！', '？', '.', ',', '!', '?', ';', '；', ' ');

        // 限长，避免摘要过长
        if (text.Length > 12)
        {
            text = text[..12].Trim();
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}