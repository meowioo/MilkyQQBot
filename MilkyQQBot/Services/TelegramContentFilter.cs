using System.Text;

namespace MilkyQQBot.Services;

/// <summary>
/// Telegram 文本内容过滤器。
/// 
/// 说明：
/// 1. 只按“文字内容”过滤，不做图片 OCR，不识别纯图片广告。
/// 2. 先用内置关键字做基础拦截，再叠加配置文件里的自定义关键字。
/// 3. 适合拦截：博彩广告、诈骗引流、色情招嫖、违禁信息、常见垃圾广告。
/// </summary>
public static class TelegramContentFilter
{
    /// <summary>
    /// 内置规则。
    /// 第一项：关键字
    /// 第二项：日志里显示的类别
    /// 
    /// 这里尽量放“强特征词”，减少误杀。
    /// </summary>
    private static readonly (string Keyword, string Category)[] DefaultRules =
    [
        // 博彩 / 赌博 / 彩票
        ("博彩", "博彩广告"),
        ("赌博", "博彩广告"),
        ("赌场", "博彩广告"),
        ("真人博彩", "博彩广告"),
        ("体育博彩", "博彩广告"),
        ("彩票计划群", "博彩广告"),
        ("彩票计划群", "博彩广告"),
        ("彩票计划群", "博彩广告"),
        ("时时彩", "博彩广告"),
        ("六合彩", "博彩广告"),
        ("幸运飞艇", "博彩广告"),
        ("北京赛车", "博彩广告"),
        ("上分", "博彩广告"),
        ("下分", "博彩广告"),
        ("客服上分", "博彩广告"),
        ("人工上分", "博彩广告"),
        ("现金网", "博彩广告"),
        ("首充送彩金", "博彩广告"),
        ("注册送彩金", "博彩广告"),
        ("充值返利", "博彩广告"),
        ("包赢", "博彩广告"),
        ("包赔", "博彩广告"),
        ("稳赚不赔", "博彩广告"),
        ("百家乐", "博彩广告"),
        ("德州扑克", "博彩广告"),

        // 广告 / 引流 / 灰产
        ("代理返佣", "广告引流"),
        ("拉你进群", "广告引流"),
        ("私聊我", "广告引流"),
        ("联系飞机", "广告引流"),
        ("电报客服", "广告引流"),
        ("飞机客服", "广告引流"),
        ("商务合作", "广告引流"),
        ("兼职代发", "广告引流"),
        ("兼职日结", "广告引流"),
        ("推广合作", "广告引流"),
        ("接推广", "广告引流"),

        // 色情 / 招嫖
        ("裸聊", "不良信息"),
        ("约炮", "不良信息"),
        ("成人视频", "不良信息"),
        ("成人视频", "不良信息"),
        ("黄网站", "不良信息"),
        ("黄色网站", "不良信息"),
        ("同城约", "不良信息"),
        ("包夜", "不良信息"),

        // 诈骗 / 虚假收益
        ("稳定盈利", "诈骗引流"),
        ("高收益", "诈骗引流"),
        ("日入过万", "诈骗引流"),
        ("轻松赚钱", "诈骗引流"),
        ("带你赚钱", "诈骗引流"),
        ("刷单返利", "诈骗引流"),
        ("投资返利", "诈骗引流"),
        ("躺赚", "诈骗引流"),
        ("保本收益", "诈骗引流"),

        // 违禁
        ("冰毒", "违禁信息"),
        ("摇头丸", "违禁信息"),
        ("枪支", "违禁信息"),
        ("办证", "违禁信息"),
        ("代开发票", "违禁信息"),
        ("出售数据", "违禁信息"),
        ("洗钱", "违禁信息")
    ];

    /// <summary>
    /// 检查一段文本是否应被拦截。
    /// 命中时返回 true，并带出命中关键字与类别。
    /// </summary>
    public static bool TryMatchBlockedText(
        string? text,
        IEnumerable<string>? customKeywords,
        out string matchedKeyword,
        out string matchedCategory)
    {
        matchedKeyword = string.Empty;
        matchedCategory = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = NormalizeText(text);
        string compact = BuildCompactText(normalized);

        // 先检查内置规则
        foreach (var rule in DefaultRules)
        {
            if (ContainsKeyword(normalized, compact, rule.Keyword))
            {
                matchedKeyword = rule.Keyword;
                matchedCategory = rule.Category;
                return true;
            }
        }

        // 再检查配置文件里的自定义关键字
        if (customKeywords != null)
        {
            foreach (string keyword in customKeywords.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                string trimmed = keyword.Trim();
                if (ContainsKeyword(normalized, compact, trimmed))
                {
                    matchedKeyword = trimmed;
                    matchedCategory = "自定义关键字";
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 判断文本中是否包含关键字。
    /// 同时检查：
    /// 1. 原始归一化文本
    /// 2. 去空格/换行/常见分隔符后的紧凑文本
    /// 
    /// 这样能拦截类似“客 服 上 分”“稳 定 盈 利”这种绕过写法。
    /// </summary>
    private static bool ContainsKeyword(string normalizedText, string compactText, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return false;

        string normalizedKeyword = NormalizeText(keyword);
        string compactKeyword = BuildCompactText(normalizedKeyword);

        return normalizedText.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase)
               || compactText.Contains(compactKeyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 基础归一化：
    /// - 转小写
    /// - 统一换行
    /// - 去零宽字符
    /// </summary>
    private static string NormalizeText(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\u200B", "")
            .Replace("\u200C", "")
            .Replace("\u200D", "")
            .Replace("\u2060", "")
            .Replace("\uFEFF", "")
            .Trim()
            .ToLowerInvariant();
    }

    /// <summary>
    /// 生成紧凑文本：
    /// 去掉空格、换行、制表符、横线、下划线、点号等常见分隔字符，
    /// 用于识别“拆开写”的广告词。
    /// </summary>
    private static string BuildCompactText(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch))
                continue;

            if (ch is '-' or '_' or '.' or '·' or '•' or '|' or '/' or '\\' or ',' or '，' or '。' or ':' or '：')
                continue;

            sb.Append(ch);
        }

        return sb.ToString();
    }
}