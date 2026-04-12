using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot.Services;

public static class TelegramNewsService
{
    private const string SeenStatePath = "telegram_news_seen.json";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly SemaphoreSlim _loopLock = new(1, 1);
    private static readonly object _stateLock = new();

    private static HashSet<string> _seenKeys = LoadSeenKeys();
    private static bool _started = false;

    public static void Start(MilkyClient milky, CancellationToken cancellationToken = default)
    {
        if (_started)
            return;

        _started = true;

        Console.WriteLine("[TelegramNews] 订阅服务启动中...");
        _ = Task.Run(async () => await RunAsync(milky, cancellationToken), cancellationToken);
    }

    private static async Task RunAsync(MilkyClient milky, CancellationToken cancellationToken)
    {
        var config = AppConfig.Current.TelegramNews;

        if (config.Feeds.Count == 0)
        {
            Console.WriteLine("[TelegramNews] 未配置任何 RSS 源，服务不会启动。");
            return;
        }

        Console.WriteLine($"[TelegramNews] 已加载 {config.Feeds.Count} 个 RSS 源，轮询间隔 {Math.Max(1, config.PollIntervalMinutes)} 分钟。");

        // 首次启动：只预热，不推送历史
        await SyncOnceAsync(milky, bootstrap: config.BootstrapWithoutPush, cancellationToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.PollIntervalMinutes)));

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await SyncOnceAsync(milky, bootstrap: false, cancellationToken);
        }
    }

    private static async Task SyncOnceAsync(MilkyClient milky, bool bootstrap, CancellationToken cancellationToken)
    {
        if (!await _loopLock.WaitAsync(0, cancellationToken))
        {
            Console.WriteLine("[TelegramNews] 上一轮同步尚未结束，跳过本次轮询。");
            return;
        }

        try
        {
            var config = AppConfig.Current.TelegramNews;
            var feeds = config.Feeds
                .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                .ToList();

            var enabledGroupIds = GroupConfigManager.GetTelegramNewsEnabledGroupIds();

            foreach (var feed in feeds)
            {
                try
                {
                    var items = await ReadFeedItemsAsync(feed, cancellationToken);

                    foreach (var item in items.OrderBy(x => x.PublishedAt))
                    {
                        string seenKey = BuildSeenKey(feed.Url, item.UniqueKey);

                        if (IsSeen(seenKey))
                            continue;

                        // 首次启动不推送历史；之后只要是新内容就推到所有开启订阅的群
                        if (!bootstrap && enabledGroupIds.Count > 0)
                        {
                            foreach (var groupId in enabledGroupIds)
                            {
                                try
                                {
                                    await SendToGroupAsync(milky, groupId, item);
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
                Console.WriteLine("[TelegramNews] 首次启动预热完成，当前存量消息已记为已读，不会补发历史内容。");
            }
        }
        finally
        {
            _loopLock.Release();
        }
    }

    private static async Task<List<TelegramNewsItem>> ReadFeedItemsAsync(TelegramFeedConfig feed, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        request.Headers.UserAgent.ParseAdd("MilkyQQBot/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = XDocument.Parse(xml);

        string channelTitle =
            doc.Root?.Element("channel")?.Element("title")?.Value?.Trim()
            ?? (!string.IsNullOrWhiteSpace(feed.Name) ? feed.Name : "Telegram频道");

        return doc
            .Descendants("item")
            .Select(x => ParseItem(x, channelTitle))
            .Where(x => x is not null)
            .Cast<TelegramNewsItem>()
            .ToList();
    }

    private static TelegramNewsItem? ParseItem(XElement itemElement, string channelTitle)
    {
        string title = (itemElement.Element("title")?.Value ?? string.Empty).Trim();
        string link = (itemElement.Element("link")?.Value ?? string.Empty).Trim();
        string guid = (itemElement.Element("guid")?.Value ?? string.Empty).Trim();

        string bodyHtml =
            itemElement.Elements().FirstOrDefault(x => x.Name.LocalName == "encoded")?.Value
            ?? itemElement.Element("description")?.Value
            ?? string.Empty;

        string plainText = HtmlToPlainText(bodyHtml);
        List<string> imageUrls = ExtractImageUrls(bodyHtml);
        List<TelegramVideoMedia> videos = ExtractVideos(bodyHtml);

        string pubDateRaw = (itemElement.Element("pubDate")?.Value ?? string.Empty).Trim();
        DateTimeOffset publishedAt =
            DateTimeOffset.TryParse(pubDateRaw, out var parsedDate)
                ? parsedDate
                : DateTimeOffset.UtcNow;

        string uniqueSeed = !string.IsNullOrWhiteSpace(guid)
            ? guid
            : !string.IsNullOrWhiteSpace(link)
                ? link
                : $"{title}|{pubDateRaw}|{plainText}";

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
            UniqueKey = Sha256(uniqueSeed),
        };
    }

    private static async Task SendToGroupAsync(MilkyClient milky, long groupId, TelegramNewsItem item)
    {
        var ctx = new CommandContext
        {
            Client = milky,
            Scene = "group",
            SenderId = 0,
            PeerId = groupId,
            Command = "/news",
            Args = Array.Empty<string>(),
            SenderRole = "Member",
            MessageSeq = 0
        };

        string text = BuildTextMessage(item);
        if (!string.IsNullOrWhiteSpace(text))
        {
            await ctx.ReplyAsync(text);
            await Task.Delay(500);
        }

        foreach (string imageUrl in item.ImageUrls
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct()
                     .Take(10))
        {
            await ctx.ReplyImageAsync(imageUrl);
            await Task.Delay(800);
        }

        foreach (var video in item.Videos
                     .Where(x => !string.IsNullOrWhiteSpace(x.Url))
                     .DistinctBy(x => x.Url)
                     .Take(3))
        {
            await ctx.ReplyVideoAsync(video.Url, video.ThumbUrl);
            await Task.Delay(1000);
        }
    }

    private static string BuildTextMessage(TelegramNewsItem item)
    {
        var sb = new StringBuilder();
        sb.AppendLine("【Telegram频道更新】");
        sb.AppendLine($"频道：{item.ChannelName}");

        if (!string.IsNullOrWhiteSpace(item.Title))
            sb.AppendLine($"标题：{item.Title}");

        sb.AppendLine($"时间：{item.PublishedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}");

        if (!string.IsNullOrWhiteSpace(item.Link))
            sb.AppendLine($"链接：{item.Link}");

        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            sb.AppendLine();
            sb.AppendLine(item.Text);
        }

        string result = sb.ToString().Trim();
        return result.Length > 3500 ? result[..3500] + "\n\n（正文过长，已截断）" : result;
    }

    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        string text = html;
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|blockquote|li|ul|ol|h1|h2|h3|h4|h5|h6)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty, RegexOptions.Singleline);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\r", string.Empty);
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static List<string> ExtractImageUrls(string html)
    {
        return Regex.Matches(
                html,
                @"<img\b[^>]*?src=['""](?<src>[^'""]+)['""][^>]*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(x => WebUtility.HtmlDecode(x.Groups["src"].Value))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
    }

    private static List<TelegramVideoMedia> ExtractVideos(string html)
    {
        var result = new List<TelegramVideoMedia>();

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
                result.Add(new TelegramVideoMedia(
                    WebUtility.HtmlDecode(src),
                    string.IsNullOrWhiteSpace(poster) ? null : WebUtility.HtmlDecode(poster)
                ));
            }
        }

        return result;
    }

    private static string GetHtmlAttribute(string attrs, string name)
    {
        return Regex.Match(
                attrs,
                $@"\b{name}\s*=\s*['""](?<value>[^'""]+)['""]",
                RegexOptions.IgnoreCase)
            .Groups["value"].Value;
    }

    private static string BuildSeenKey(string feedUrl, string uniqueKey)
        => Sha256(feedUrl + "::" + uniqueKey);

    private static string Sha256(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

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

    private static bool IsSeen(string key)
    {
        lock (_stateLock)
        {
            return _seenKeys.Contains(key);
        }
    }

    private static void MarkSeen(string key)
    {
        lock (_stateLock)
        {
            if (!_seenKeys.Add(key))
                return;

            string json = JsonSerializer.Serialize(_seenKeys, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SeenStatePath, json);
        }
    }

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

    private sealed record TelegramVideoMedia(string Url, string? ThumbUrl);
}