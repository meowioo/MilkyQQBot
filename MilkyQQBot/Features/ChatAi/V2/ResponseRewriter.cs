using System.Text.RegularExpressions;

namespace MilkyQQBot.Features.ChatAi.V2;

/// <summary>
/// 回复重写器：
/// 目标不是“改写得很聪明”，而是把模型生成的
/// 过长、过正式、过解释型的回复，压成更像群聊的一句短话。
/// </summary>
public static class ResponseRewriter
{
    /// <summary>
    /// 对模型原始回复做一次轻量重写。
    /// 返回 RewriteResult，便于上层记录日志。
    /// </summary>
    public static RewriteResult Rewrite(string rawReply)
    {
        if (string.IsNullOrWhiteSpace(rawReply))
        {
            return RewriteResult.Keep("");
        }

        string original = rawReply.Trim();
        string text = original;

        // 1. 去掉明显的 AI 书面话术
        text = RemoveFormalPrefixes(text);

        // 2. 把常见书面表达替换成更口语的说法
        text = ReplaceFormalPhrases(text);

        // 3. 只保留第一句，避免长篇解释
        text = KeepOnlyFirstSentence(text);

        // 4. 压缩多余空白和标点
        text = NormalizePunctuation(text);

        // 5. 超长时做一次硬截断，尽量留短句
        text = TrimToLength(text, 24);

        // 6. 兜底：如果重写后为空，就回退到原句的短截断版
        if (string.IsNullOrWhiteSpace(text))
        {
            text = TrimToLength(original, 24);
        }

        // 判断是否真的发生了重写
        if (text == original)
        {
            return RewriteResult.Keep(text);
        }

        return RewriteResult.Rewritten(original, text);
    }

    /// <summary>
    /// 去掉典型“客服腔 / 解释腔”开头。
    /// </summary>
    private static string RemoveFormalPrefixes(string text)
    {
        string[] prefixes =
        {
            "根据你提供的信息，",
            "根据你提供的信息",
            "从这个角度来看，",
            "从这个角度来看",
            "我认为，",
            "我认为",
            "我觉得，",
            "我觉得",
            "总的来说，",
            "总的来说",
            "总之，",
            "总之",
            "以下是",
            "建议你",
            "需要注意的是，",
            "需要注意的是"
        };

        foreach (var prefix in prefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        return text;
    }

    /// <summary>
    /// 把较正式的表达替换成更短、更口语的表达。
    /// </summary>
    private static string ReplaceFormalPhrases(string text)
    {
        var replacements = new Dictionary<string, string>
        {
            ["首先"] = "",
            ["其次"] = "",
            ["最后"] = "",
            ["因此"] = "所以",
            ["例如"] = "比如",
            ["建议"] = "可以",
            ["可能是由于"] = "多半是",
            ["可能是因为"] = "多半是",
            ["这意味着"] = "这说明",
            ["这种情况"] = "这情况",
            ["并且"] = "而且",
            ["但是"] = "但",
            ["如果你想"] = "你要是想",
            ["可以考虑"] = "可以直接",
            ["需要注意"] = "注意",
            ["建议你先"] = "你先",
            ["我认为可以"] = "可以",
            ["从而"] = "这样就"
        };

        foreach (var pair in replacements)
        {
            text = text.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return text.Trim();
    }

    /// <summary>
    /// 只保留第一句，避免一口气输出多句解释。
    /// </summary>
    private static string KeepOnlyFirstSentence(string text)
    {
        // 按中文和英文常见断句符截断
        char[] separators = { '。', '！', '？', '.', '!', '?', '\n', '\r' };

        int idx = text.IndexOfAny(separators);
        if (idx > 0)
        {
            text = text[..idx];
        }

        // 遇到逗号太多，也只保留前半段
        var commaParts = text.Split('，', StringSplitOptions.RemoveEmptyEntries);
        if (commaParts.Length >= 3)
        {
            text = string.Join('，', commaParts.Take(2));
        }

        return text.Trim('，', '。', '！', '？', '；', ';', ' ');
    }

    /// <summary>
    /// 规范标点和空白，避免重写后出现奇怪格式。
    /// </summary>
    private static string NormalizePunctuation(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // 连续标点压缩
        text = Regex.Replace(text, @"[，,]{2,}", "，");
        text = Regex.Replace(text, @"[。\.]{2,}", "。");
        text = Regex.Replace(text, @"[!！]{2,}", "!");
        text = Regex.Replace(text, @"[?？]{2,}", "?");

        // 去掉前后多余符号
        text = text.Trim('，', '。', '！', '？', ';', '；', ',', '.', ' ');

        return text;
    }

    /// <summary>
    /// 把句子压到指定长度内。
    /// 中文群聊里太长会很像 AI。
    /// </summary>
    private static string TrimToLength(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Trim();

        if (text.Length <= maxLength)
            return text;

        return text[..maxLength].TrimEnd() + "…";
    }
}

/// <summary>
/// 重写结果对象，方便打印日志和后续统计。
/// </summary>
public sealed class RewriteResult
{
    public bool WasRewritten { get; init; }
    public string OriginalText { get; init; } = "";
    public string FinalText { get; init; } = "";

    public static RewriteResult Keep(string text) =>
        new()
        {
            WasRewritten = false,
            OriginalText = text,
            FinalText = text
        };

    public static RewriteResult Rewritten(string original, string final) =>
        new()
        {
            WasRewritten = true,
            OriginalText = original,
            FinalText = final
        };
}