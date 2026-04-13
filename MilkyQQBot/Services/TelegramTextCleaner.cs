using System.Text;
using TL;

namespace MilkyQQBot.Services;

/// <summary>
/// Telegram 文本清洗与超链接格式化工具。
/// 
/// 转发规则：
/// 1. 普通文本尽量保持原样。
/// 2. 外部网站超链接：显示为 “文字内容[链接]”。
/// 3. Telegram 内部跳转链接（如 https://t.me/aaa）：显示为 “文字内容[@aaa]”。
/// 4. 纯链接文本：
///    - 如果是 t.me 内链，则显示为 @aaa
///    - 如果是外部链接，则保留原链接
/// 5. 自动清理零宽字符、控制字符、多余空行。
/// </summary>
public static class TelegramTextCleaner
{
    /// <summary>
    /// 按项目规则把 Telegram Message 转成适合发到 QQ 的文本。
    /// </summary>
    public static string BuildText(Message message)
    {
        string rawText = message.message ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        MessageEntity[] entities = message.entities ?? Array.Empty<MessageEntity>();
        if (entities.Length == 0)
            return Clean(rawText);

        var orderedEntities = entities
            .OrderBy(x => x.offset)
            .ThenBy(x => x.length)
            .ToArray();

        var sb = new StringBuilder();
        int cursor = 0;

        foreach (MessageEntity entity in orderedEntities)
        {
            int start = Math.Clamp(entity.offset, 0, rawText.Length);
            int end = Math.Clamp(entity.offset + entity.length, start, rawText.Length);

            // 已经被前一个实体覆盖的内容，直接跳过
            if (end <= cursor)
                continue;

            // 先补上实体前面的普通文本
            if (start > cursor)
            {
                sb.Append(rawText.AsSpan(cursor, start - cursor));
            }

            string entityText = rawText.Substring(start, end - start);
            sb.Append(TransformEntityText(entity, entityText));

            cursor = end;
        }

        // 补上最后剩余的普通文本
        if (cursor < rawText.Length)
        {
            sb.Append(rawText.AsSpan(cursor));
        }

        return Clean(sb.ToString());
    }

    /// <summary>
    /// 清洗普通文本：
    /// - 统一换行
    /// - 去零宽字符
    /// - 去控制字符
    /// - 压缩多余空行
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string normalized = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\u2060", "")
            .Replace("\uFEFF", "");

        var sb = new StringBuilder(normalized.Length);

        foreach (char ch in normalized)
        {
            // 保留换行和制表符，其它控制字符去掉
            if (ch == '\n' || ch == '\t' || !char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        List<string> lines = sb.ToString()
            .Split('\n', StringSplitOptions.None)
            .Select(x => x.TrimEnd())
            .ToList();

        // 去掉首尾空行
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
            lines.RemoveAt(0);

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        // 连续空行最多保留 1 个
        var resultLines = new List<string>();
        int blankCount = 0;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankCount++;
                if (blankCount <= 1)
                    resultLines.Add(string.Empty);
            }
            else
            {
                blankCount = 0;
                resultLines.Add(line);
            }
        }

        return string.Join("\n", resultLines).Trim();
    }

    /// <summary>
    /// 把 Telegram 的单个实体转换成符合 QQ 转发规则的文本。
    /// </summary>
    private static string TransformEntityText(MessageEntity entity, string entityText)
    {
        switch (entity)
        {
            // 文本显示和链接地址分离
            // 例如：显示“你好”，实际链接是 https://t.me/aaa
            case MessageEntityTextUrl textUrl:
                return entityText + FormatLinkSuffix(textUrl.url);

            // 文本里直接写了 URL
            case MessageEntityUrl:
                if (TryExtractTelegramHandle(entityText, out string handle))
                    return "@" + handle;

                return entityText;

            default:
                return entityText;
        }
    }

    /// <summary>
    /// 按你的规则输出链接后缀：
    /// - Telegram 内部链接 -> [@aaa]
    /// - 外部网站链接 -> [实际网址]
    /// </summary>
    private static string FormatLinkSuffix(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        string normalizedUrl = url.Trim();

        if (TryExtractTelegramHandle(normalizedUrl, out string handle))
            return $"[@{handle}]";

        return $"[{normalizedUrl}]";
    }

    /// <summary>
    /// 从 Telegram 内链中提取用户名。
    /// 支持：
    /// - https://t.me/aaa
    /// - http://t.me/aaa
    /// - t.me/aaa
    /// - https://t.me/s/aaa
    /// </summary>
    private static bool TryExtractTelegramHandle(string url, out string handle)
    {
        handle = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        string normalized = url.Trim();

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.StartsWith("t.me/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("telegram.me/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri))
            return false;

        string host = uri.Host.ToLowerInvariant();
        if (host is not "t.me" and not "www.t.me" and not "telegram.me" and not "www.telegram.me")
            return false;

        string[] segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return false;

        // /aaa
        if (string.Equals(segments[0], "s", StringComparison.OrdinalIgnoreCase))
        {
            // /s/aaa
            if (segments.Length >= 2)
            {
                handle = segments[1].TrimStart('@');
                return !string.IsNullOrWhiteSpace(handle);
            }

            return false;
        }

        // 过滤掉一些不适合当用户名的特殊路径
        if (segments[0] is "c" or "joinchat" or "+" or "addstickers")
            return false;

        handle = segments[0].TrimStart('@');
        return !string.IsNullOrWhiteSpace(handle);
    }
}