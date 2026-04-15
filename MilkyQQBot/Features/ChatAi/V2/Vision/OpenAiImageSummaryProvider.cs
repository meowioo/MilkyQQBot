using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MilkyQQBot.Features.ChatAi.V2.Vision;

/// <summary>
/// 基于 OpenAI 兼容接口的图片摘要提供器。
/// 只要求你的接口支持 image_url 输入即可。
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
        _apiUrl = apiUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
    }

    public async Task<string?> GenerateSummaryAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return null;

        var requestBody = new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "你是一个群聊图片摘要器。请只输出一句很短的中文摘要，不要解释，不要标点，不要超过12个字。优先判断类型，如：聊天截图、梗图、表情包、游戏截图、宠物照片、风景照、商品图、代码截图、文档截图。能更具体就更具体，例如：猫咪表情包、王者战绩图。"
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
                                url = imageUrl
                            }
                        }
                    }
                }
            },
            temperature = 0.2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

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
    /// 把模型输出压成适合写回数据库的短摘要。
    /// </summary>
    private static string? NormalizeSummary(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        string text = content.Trim();

        // 去掉换行和多余空白
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        // 去掉句末标点，尽量短
        text = text.Trim('。', '，', '！', '？', '.', ',', '!', '?', ';', '；', ' ');

        // 截断，避免模型话太多
        if (text.Length > 12)
        {
            text = text[..12].Trim();
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}