using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot.Services;

/// <summary>
/// Telegram 频道订阅服务：
/// 1. 定时轮询 RSSHub 的 Telegram 频道 XML
/// 2. 解析正文、图片、视频
/// 3. 去重后推送到已开启订阅的QQ群
///
/// 当前版本规则：
/// - 不发送“频道 / 标题 / 时间 / 链接”等头部信息
/// - 只发送正文内容
/// - 文字 + 图片：无论多少张图片，都尽量放在同一条消息里发送
/// - 文字 + 视频：由于 QQ 限制，先发文字，再逐个发视频
/// - 纯图片消息里附带的 GMT 时间行会自动去掉
/// - 文件消息（📄 / zip / rar / 7z / pdf / docx 等）直接忽略
/// - 转发消息（🔁）会自动去掉 "Forwarded From ..." 那一段
/// - 回复消息（↩️）会自动去掉 rsshub-quote 引用块
/// </summary>
public static class TelegramNewsService
{
    /// <summary>
    /// 已推送消息的去重状态文件
    /// </summary>
    private const string SeenStatePath = "telegram_news_seen.json";

    /// <summary>
    /// HTTP 客户端，用于请求 RSSHub XML
    /// </summary>
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// 防止轮询重入
    /// </summary>
    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// 保护已读状态 HashSet 的并发读写
    /// </summary>
    private static readonly object _seenLock = new();

    /// <summary>
    /// 已推送过的消息键
    /// </summary>
    private static HashSet<string> _seenKeys = LoadSeenKeys();

    /// <summary>
    /// 保证服务只启动一次
    /// </summary>
    private static bool _started;

    /// <summary>
    /// 启动 Telegram 订阅服务
    /// </summary>
    public static void Start(MilkyClient milky, CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        _started = true;

        Console.WriteLine("[TelegramNews] 订阅服务启动中...");
        _ = Task.Run(() => RunAsync(milky, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 后台轮询主循环
    /// </summary>
    private static async Task RunAsync(MilkyClient milky, CancellationToken cancellationToken)
    {
        var config = AppConfig.Current.TelegramNews;

        if (config == null || config.Feeds == null || config.Feeds.Count == 0)
        {
            Console.WriteLine("[TelegramNews] 未配置任何 RSS 源，服务不会启动。");
            return;
        }

        int intervalMinutes = Math.Max(1, config.PollIntervalMinutes);
        Console.WriteLine($"[TelegramNews] 已加载 {config.Feeds.Count} 个 RSS 源，轮询间隔 {intervalMinutes} 分钟。");

        // 首次启动：建立已读基线，不推送历史消息
        await SyncOnceAsync(milky, bootstrap: config.BootstrapWithoutPush, cancellationToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await SyncOnceAsync(milky, bootstrap: false, cancellationToken);
        }
    }

    /// <summary>
    /// 执行一轮同步
    /// </summary>
    private static async Task SyncOnceAsync(MilkyClient milky, bool bootstrap, CancellationToken cancellationToken)
    {
        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            Console.WriteLine("[TelegramNews] 上一轮同步尚未结束，跳过本次轮询。");
            return;
        }

        try
        {
            var config = AppConfig.Current.TelegramNews;
            if (config == null || config.Feeds == null || config.Feeds.Count == 0)
                return;

            var enabledGroupIds = GroupConfigManager
                .GetTelegramNewsEnabledGroupIds()
                .ToArray();

            foreach (var feed in config.Feeds.Where(x => !string.IsNullOrWhiteSpace(x.Url)))
            {
                try
                {
                    var items = await ReadFeedItemsAsync(feed, cancellationToken);

                    foreach (var item in items.OrderBy(x => x.PublishedAt))
                    {
                        string seenKey = BuildSeenKey(feed.Url, item.UniqueKey);

                        if (IsSeen(seenKey))
                            continue;

                        if (!bootstrap && enabledGroupIds.Length > 0)
                        {
                            foreach (long groupId in enabledGroupIds)
                            {
                                try
                                {
                                    await SendToGroupAsync(milky, groupId, item, cancellationToken);
                                    await Task.Delay(1000, cancellationToken);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[TelegramNews] 推送到群 {groupId} 失败: {ex.Message}");
                                }
                            }
                        }

                        MarkSeen(seenKey);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TelegramNews] 读取源失败: {feed.Url} | {ex.Message}");
                }
            }

            if (bootstrap)
            {
                Console.WriteLine("[TelegramNews] 首次启动预热完成，当前存量消息已记为已读，不补发历史内容。");
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// 读取单个 RSS 源，并解析为消息列表
    /// </summary>
    private static async Task<List<TelegramNewsItem>> ReadFeedItemsAsync(
        TelegramFeedConfig feed,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        request.Headers.UserAgent.ParseAdd("MilkyQQBot/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = XDocument.Parse(xml);

        string channelTitle =
            doc.Root?.Element("channel")?.Element("title")?.Value?.Trim()
            ?? (string.IsNullOrWhiteSpace(feed.Name) ? "Telegram频道" : feed.Name);

        return doc.Descendants("item")
            .Select(x => ParseItem(x, channelTitle))
            .Where(x => x != null)
            .Cast<TelegramNewsItem>()
            .ToList();
    }

    /// <summary>
    /// 将 RSS item 节点解析成内部消息对象
    /// </summary>
    private static TelegramNewsItem? ParseItem(XElement itemElement, string channelTitle)
    {
        string title = (itemElement.Element("title")?.Value ?? string.Empty).Trim();
        string link = (itemElement.Element("link")?.Value ?? string.Empty).Trim();
        string guid = (itemElement.Element("guid")?.Value ?? string.Empty).Trim();

        string bodyHtml =
            itemElement.Elements().FirstOrDefault(x => x.Name.LocalName == "encoded")?.Value
            ?? itemElement.Element("description")?.Value
            ?? string.Empty;

        // 如果标题带 🔁，代表这是一条转发消息
        // 先从 HTML 中去掉最前面的 Forwarded From 段落，避免被解析进正文
        if (IsForwardedTitle(title))
        {
            bodyHtml = RemoveForwardedFromBlock(bodyHtml);
        }

        // 如果标题带 ↩️，代表这是一条回复消息
        // 先从 HTML 中去掉最前面的 rsshub-quote 引用块，避免被解析进正文
        if (IsReplyTitle(title))
        {
            bodyHtml = RemoveReplyQuoteBlock(bodyHtml);
        }

        string pubDateRaw = (itemElement.Element("pubDate")?.Value ?? string.Empty).Trim();
        DateTimeOffset publishedAt =
            DateTimeOffset.TryParse(pubDateRaw, out var parsedDate)
                ? parsedDate
                : DateTimeOffset.UtcNow;

        string plainText = HtmlToPlainText(bodyHtml);

        // 再做一次兜底清理，防止不同格式的 Forwarded From 残留进正文
        if (IsForwardedTitle(title))
        {
            plainText = RemoveForwardedFromText(plainText);
        }

        // 再做一次兜底清理，防止回复引用残留进正文
        if (IsReplyTitle(title))
        {
            plainText = RemoveReplyQuoteText(plainText);
        }

        var imageUrls = ExtractImageUrls(bodyHtml);
        var videos = ExtractVideos(bodyHtml);

        // 文件消息直接忽略
        if (IsFileOnlyMessage(title))
            return null;

        string uniqueSeed = !string.IsNullOrWhiteSpace(guid)
            ? guid
            : !string.IsNullOrWhiteSpace(link)
                ? link
                : $"{title}|{pubDateRaw}|{plainText}|{string.Join(",", imageUrls)}|{string.Join(",", videos.Select(x => x.Url))}";

        if (string.IsNullOrWhiteSpace(uniqueSeed))
            return null;

        return new TelegramNewsItem
        {
            ChannelName = channelTitle,
            Title = title,
            Link = link,
            PublishedAt = publishedAt,
            Text = plainText,
            ImageUrls = imageUrls,
            Videos = videos,
            UniqueKey = Sha256(uniqueSeed)
        };
    }

    /// <summary>
    /// 判断是否为转发消息标题
    /// 🔁：代表转发
    /// </summary>
    private static bool IsForwardedTitle(string title)
    {
        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("🔁", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断是否为回复消息标题
    /// ↩️：代表回复
    /// </summary>
    private static bool IsReplyTitle(string title)
    {
        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("↩️", StringComparison.Ordinal);
    }

    /// <summary>
    /// 从 HTML 中移除转发来源段落。
    /// 例如：
    /// <p>Forwarded From ...</p>
    /// </summary>
    private static string RemoveForwardedFromBlock(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        string result = html;

        // 去掉开头连续出现的 Forwarded From 段落
        result = Regex.Replace(
            result,
            @"^\s*(<p>\s*Forwarded From.*?</p>\s*)+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return result.Trim();
    }

    /// <summary>
    /// 从 HTML 中移除回复引用块。
    /// 例如：
    /// <div class="rsshub-quote"><blockquote>...</blockquote></div>
    /// 
    /// 这类内容是 Telegram 回复消息里的“被回复内容”，
    /// 群转发时不需要保留。
    /// </summary>
    private static string RemoveReplyQuoteBlock(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        string result = html;

        // 去掉开头连续出现的 rsshub-quote 引用块
        result = Regex.Replace(
            result,
            @"^\s*(<div\b[^>]*class\s*=\s*['""][^'""]*\brsshub-quote\b[^'""]*['""][^>]*>.*?</div>\s*)+",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return result.Trim();
    }

    /// <summary>
    /// 从纯文本中移除最前面的 "Forwarded From ..." 行
    /// </summary>
    private static string RemoveForwardedFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        while (true)
        {
            int firstNewLineIndex = normalized.IndexOf('\n');
            string firstLine;
            string remaining;

            if (firstNewLineIndex < 0)
            {
                firstLine = normalized.Trim();
                remaining = string.Empty;
            }
            else
            {
                firstLine = normalized[..firstNewLineIndex].Trim();
                remaining = normalized[(firstNewLineIndex + 1)..].TrimStart();
            }

            if (!firstLine.StartsWith("Forwarded From ", StringComparison.OrdinalIgnoreCase))
                break;

            normalized = remaining;

            if (string.IsNullOrWhiteSpace(normalized))
                break;
        }

        return normalized.Trim();
    }

    /// <summary>
    /// 从纯文本中移除回复引用残留。
    /// 
    /// 经过 RemoveReplyQuoteBlock 后，大多数情况不会再残留。
    /// 这里额外做一次兜底，避免某些格式变化时还留下：
    /// - “某某:”
    /// - “Photo / Video / File ...”
    /// 这种引用摘要。
    /// </summary>
    private static string RemoveReplyQuoteText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        var lines = normalized
            .Split('\n', StringSplitOptions.None)
            .Select(x => x.Trim())
            .ToList();

        // 去掉开头的空行
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);

        if (lines.Count == 0)
            return string.Empty;

        // 常见的引用摘要第二行
        bool secondLineLooksLikeQuotedMedia =
            lines.Count >= 2 &&
            (
                lines[1].Equals("Photo", StringComparison.OrdinalIgnoreCase) ||
                lines[1].Equals("Video", StringComparison.OrdinalIgnoreCase) ||
                lines[1].Equals("File", StringComparison.OrdinalIgnoreCase) ||
                lines[1].Equals("Document", StringComparison.OrdinalIgnoreCase) ||
                lines[1].Equals("Sticker", StringComparison.OrdinalIgnoreCase) ||
                lines[1].Equals("GIF", StringComparison.OrdinalIgnoreCase)
            );

        // 第一行常见形式：频道名:
        bool firstLineLooksLikeQuotedSender =
            lines[0].EndsWith(":", StringComparison.Ordinal) ||
            lines[0].EndsWith("：", StringComparison.Ordinal);

        if (firstLineLooksLikeQuotedSender && secondLineLooksLikeQuotedMedia)
        {
            lines.RemoveAt(0);
            lines.RemoveAt(0);

            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
                lines.RemoveAt(0);
        }

        return string.Join("\n", lines).Trim();
    }

    /// <summary>
    /// 判断当前 item 是否为“文件消息”。
    ///
    /// 标记说明：
    /// 🔁：转发
    /// 🖼：有图片
    /// 🎬：有视频
    /// 📄：有文件
    ///
    /// 文件消息直接忽略，不进行转发。
    /// </summary>
    private static bool IsFileOnlyMessage(string title)
    {
        var titleText = title?.Trim() ?? string.Empty;

        // 只要标题里有 📄，就视为文件消息
        return titleText.Contains("📄", StringComparison.Ordinal);
    }

    /// <summary>
    /// 发送到指定群
    ///
    /// 规则：
    /// 1. 只显示正文，不显示频道/标题/时间/链接头部
    /// 2. 文字 + 图片：无论多少张图片，都一起发
    /// 3. 文字 + 视频：先发文字，再发视频
    /// 4. 图片 + 视频同时存在时：先发 文字+图片，再发视频
    /// 5. 纯图片：多张图片一起发
    /// 6. 纯视频：逐个发送视频
    /// </summary>
    private static async Task SendToGroupAsync(
        MilkyClient milky,
        long groupId,
        TelegramNewsItem item,
        CancellationToken cancellationToken)
    {
        var ctx = CommandContext.CreateGroup(milky, groupId);

        string text = BuildDisplayText(item);

        var images = DistinctNonEmpty(item.ImageUrls).Take(10).ToList();
        var videos = DistinctVideos(item.Videos).Take(3).ToList();

        bool hasText = !string.IsNullOrWhiteSpace(text);
        bool hasImages = images.Count > 0;
        bool hasVideos = videos.Count > 0;

        // 只有文字
        if (hasText && !hasImages && !hasVideos)
        {
            await ctx.TextAsync(text);
            return;
        }

        // 只有图片 或 文字+图片
        // 无论多少张图片，都在一条消息里发出去
        if (hasImages)
        {
            var segments = new List<OutgoingSegment>();

            if (hasText)
                segments.Add(CommandContext.Seg.Text(text));

            foreach (string imageUrl in images)
            {
                segments.Add(CommandContext.Seg.Image(imageUrl));
            }

            await ctx.SendAsync(segments.ToArray());
            await Task.Delay(800, cancellationToken);

            // 如果同一条内容里还带视频，则视频单独发
            if (hasVideos)
            {
                foreach (var video in videos)
                {
                    await ctx.VideoAsync(video.Url, video.ThumbUrl);
                    await Task.Delay(1000, cancellationToken);
                }
            }

            return;
        }

        // 只有视频 或 文字+视频
        // 由于 QQ 限制：文字和 video 不能合并，所以先发文字，再逐个发视频
        if (hasVideos)
        {
            if (hasText)
            {
                await ctx.TextAsync(text);
                await Task.Delay(500, cancellationToken);
            }

            foreach (var video in videos)
            {
                await ctx.VideoAsync(video.Url, video.ThumbUrl);
                await Task.Delay(1000, cancellationToken);
            }

            return;
        }

        // 没有文字、没有图片、没有视频时不发送
    }

    /// <summary>
    /// 构建最终发送到QQ群里的正文内容
    ///
    /// 规则：
    /// - 只使用正文，不再拼“频道/时间/链接”等头部信息
    /// - 自动去掉正文最前面的 GMT 时间行
    /// - 如果正文为空，则尝试使用标题作为兜底
    /// - 但媒体标记标题要忽略或清理
    /// </summary>
    private static string BuildDisplayText(TelegramNewsItem item)
    {
        string bodyText = NormalizeDisplayText(item.Text);
        if (!string.IsNullOrWhiteSpace(bodyText))
            return bodyText;

        string fallbackTitle = NormalizeTitle(item.Title);
        return fallbackTitle;
    }

    /// <summary>
    /// 清理正文文本：
    /// 1. 去掉首行的 GMT 时间文字
    /// 2. 去掉残留的 Forwarded From 行
    /// 3. 去掉残留的回复引用摘要
    /// 4. 去掉多余空行
    /// 5. 限制最大长度
    /// </summary>
    private static string NormalizeDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string result = text.Trim();

        // 去掉纯图片消息前面附带的 GMT 时间行
        result = RemoveLeadingDateLine(result);

        // 去掉可能残留的 Forwarded From 行
        result = RemoveForwardedFromText(result);

        // 去掉可能残留的回复引用摘要
        result = RemoveReplyQuoteText(result);

        // 压缩多余空行
        result = Regex.Replace(result, @"\n{3,}", "\n\n").Trim();

        if (result.Length > 3500)
            result = result[..3500] + Environment.NewLine + Environment.NewLine + "（正文过长，已截断）";

        return result.Trim();
    }

    /// <summary>
    /// 如果正文第一行是类似：
    /// Sun, 12 Apr 2026 15:15:22 GMT
    /// 这种 RFC1123/GMT 日期，则移除这一行。
    /// </summary>
    private static string RemoveLeadingDateLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        int firstNewLineIndex = normalized.IndexOf('\n');
        if (firstNewLineIndex < 0)
        {
            return IsRfc1123DateLine(normalized) ? string.Empty : normalized;
        }

        string firstLine = normalized[..firstNewLineIndex].Trim();
        string remaining = normalized[(firstNewLineIndex + 1)..].TrimStart();

        return IsRfc1123DateLine(firstLine) ? remaining : normalized;
    }

    /// <summary>
    /// 判断一行文本是否是 RFC1123 风格的 GMT 时间
    /// 例如：
    /// Sun, 12 Apr 2026 15:15:22 GMT
    /// </summary>
    private static bool IsRfc1123DateLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        line = line.Trim();

        if (!line.EndsWith("GMT", StringComparison.OrdinalIgnoreCase))
            return false;

        return DateTimeOffset.TryParseExact(
            line,
            "r",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _);
    }

    /// <summary>
    /// 规范化标题：
    /// - 如果标题为空，返回空字符串
    /// - 去掉前缀媒体标记：🔁 ↩️ 🖼 🎬 📄 等
    /// - 如果清理后为空，则返回空字符串
    /// </summary>
    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        string normalized = title.Trim();

        // 去掉前缀媒体标记
        normalized = Regex.Replace(
            normalized,
            @"^(?:(?:🔁|↩️|🖼️?|🎬|📄|📷|🎥|📹|🎞)\s*)+",
            string.Empty,
            RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (IsMediaPlaceholderTitle(normalized))
            return string.Empty;

        return normalized;
    }

    /// <summary>
    /// 判断标题是否只是媒体占位符
    /// </summary>
    private static bool IsMediaPlaceholderTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return true;

        title = title.Trim();

        return title is "🖼"
            or "🖼️"
            or "📷"
            or "🎥"
            or "📹"
            or "🎞"
            or "🎬"
            or "📄"
            or "🔁"
            or "↩️";
    }

    /// <summary>
    /// 将 HTML 正文转成纯文本
    /// </summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        string text = html;

        // br 换行
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);

        // 常见块级标签闭合时换行
        text = Regex.Replace(
            text,
            @"</\s*(p|div|blockquote|li|ul|ol|h1|h2|h3|h4|h5|h6)\s*>",
            "\n",
            RegexOptions.IgnoreCase);

        // 去掉所有 HTML 标签
        text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);

        // 解码 HTML 实体
        text = WebUtility.HtmlDecode(text);

        // 统一换行
        text = text.Replace("\r", string.Empty);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>
    /// 从 HTML 中提取图片地址
    /// </summary>
    private static List<string> ExtractImageUrls(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new List<string>();

        return Regex.Matches(
                html,
                @"<img\b[^>]*?src=['""](?<src>[^'""]+)['""][^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(x => WebUtility.HtmlDecode(x.Groups["src"].Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 从 HTML 中提取视频地址和封面地址
    /// </summary>
    private static List<TelegramVideoMedia> ExtractVideos(string html)
    {
        var result = new List<TelegramVideoMedia>();

        if (string.IsNullOrWhiteSpace(html))
            return result;

        foreach (Match match in Regex.Matches(
                     html,
                     @"<video\b(?<attrs>[^>]*)>(?<inner>.*?)</video>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            string attrs = match.Groups["attrs"].Value;
            string inner = match.Groups["inner"].Value;

            string src = GetHtmlAttribute(attrs, "src");

            if (string.IsNullOrWhiteSpace(src))
            {
                src = Regex.Match(
                        inner,
                        @"<source\b[^>]*?src=['""](?<src>[^'""]+)['""][^>]*>",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline)
                    .Groups["src"].Value;
            }

            string poster = GetHtmlAttribute(attrs, "poster");

            if (!string.IsNullOrWhiteSpace(src))
            {
                result.Add(new TelegramVideoMedia
                {
                    Url = WebUtility.HtmlDecode(src),
                    ThumbUrl = string.IsNullOrWhiteSpace(poster)
                        ? null
                        : WebUtility.HtmlDecode(poster)
                });
            }
        }

        return result;
    }

    /// <summary>
    /// 取出 HTML 属性值
    /// </summary>
    private static string GetHtmlAttribute(string attrs, string name)
    {
        return Regex.Match(
                attrs,
                $@"\b{name}\s*=\s*['""](?<value>[^'""]+)['""]",
                RegexOptions.IgnoreCase)
            .Groups["value"].Value;
    }

    /// <summary>
    /// 去重并过滤空图片地址
    /// </summary>
    private static IEnumerable<string> DistinctNonEmpty(IEnumerable<string> source)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item))
                continue;

            if (seen.Add(item))
                yield return item;
        }
    }

    /// <summary>
    /// 去重视频（按视频 URL）
    /// </summary>
    private static IEnumerable<TelegramVideoMedia> DistinctVideos(IEnumerable<TelegramVideoMedia> source)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Url))
                continue;

            if (seen.Add(item.Url))
                yield return item;
        }
    }

    /// <summary>
    /// 构造去重键
    /// </summary>
    private static string BuildSeenKey(string feedUrl, string uniqueKey)
    {
        return Sha256(feedUrl + "::" + uniqueKey);
    }

    /// <summary>
    /// 计算 SHA256
    /// </summary>
    private static string Sha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 从本地文件加载已推送记录
    /// </summary>
    private static HashSet<string> LoadSeenKeys()
    {
        try
        {
            if (!File.Exists(SeenStatePath))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string json = File.ReadAllText(SeenStatePath);
            return JsonSerializer.Deserialize<HashSet<string>>(json)
                   ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 判断消息是否已推送
    /// </summary>
    private static bool IsSeen(string key)
    {
        lock (_seenLock)
        {
            return _seenKeys.Contains(key);
        }
    }

    /// <summary>
    /// 标记消息为已推送，并写入本地文件
    /// </summary>
    private static void MarkSeen(string key)
    {
        lock (_seenLock)
        {
            if (!_seenKeys.Add(key))
                return;

            string json = JsonSerializer.Serialize(
                _seenKeys,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(SeenStatePath, json);
        }
    }

    /// <summary>
    /// 内部消息模型
    /// </summary>
    private sealed class TelegramNewsItem
    {
        public string ChannelName { get; set; } = "";
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public DateTimeOffset PublishedAt { get; set; }
        public string Text { get; set; } = "";
        public List<string> ImageUrls { get; set; } = new();
        public List<TelegramVideoMedia> Videos { get; set; } = new();
        public string UniqueKey { get; set; } = "";
    }

    /// <summary>
    /// 视频媒体模型
    /// </summary>
    private sealed class TelegramVideoMedia
    {
        public string Url { get; set; } = "";
        public string? ThumbUrl { get; set; }
    }
}