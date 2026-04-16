using MilkyQQBot.Features.ChatAi.V2.Models;

namespace MilkyQQBot.Features.ChatAi.V2;

/// <summary>
/// V2 上下文构造器：
/// 1. 根据当前触发场景决定取多少条历史
/// 2. 调用 DatabaseManager 的“面向 AI 的上下文查询”
/// 3. 返回给 AiChatService 的 List<string>
/// </summary>
public static class V2ContextBuilder
{
    public static List<string> Build(ChatAiInput input, GroupConversationState conv)
    {
        int windowSize = ResolveWindowSize(input, conv);

        // 从数据库中取“适合喂给 AI”的上下文，而不是纯文本历史
        var context = DatabaseManager.GetRecentGroupMessagesForAi(input.GroupId, windowSize);

        // 兜底：避免极端情况下上下文为空
        if (context.Count == 0 && !string.IsNullOrWhiteSpace(input.PlainText))
        {
            context.Add($"[{input.SenderId}][{input.SenderNickname}]:{input.PlainText.Trim()}");
        }

        return context;
    }

    /// <summary>
    /// 根据不同场景，动态决定上下文窗口大小
    /// </summary>
    private static int ResolveWindowSize(ChatAiInput input, GroupConversationState conv)
    {
        // 被 @ 时，优先看最近较短的一段上下文
        if (input.IsBotMentioned)
            return 12;

        // 机器人已经参与当前话题，适当扩大窗口，便于跟进
        if (conv.BotEngaged)
            return 18;

        // 新话题形成时，窗口略小一点，减少旧话题污染
        if (conv.RecentParticipantIds.Count >= 2)
            return 10;

        // 单人测试或普通弱触发
        return 8;
    }

    /// <summary>
    /// 给触发器使用的最近消息窗口。
    /// 这里也走增强版上下文，而不是旧的纯文本历史。
    /// 这样触发器至少能感知到“图片 / 表情 / 回复”这些占位信息。
    /// </summary>
    public static List<string> BuildForTrigger(ChatAiInput input, int limit = 3)
    {
        var context = DatabaseManager.GetRecentGroupMessagesForAi(input.GroupId, limit);

        // 兜底：如果数据库里还没形成历史，就补上当前消息
        if (context.Count == 0 && !string.IsNullOrWhiteSpace(input.PlainText))
        {
            context.Add($"[{input.SenderId}][{input.SenderNickname}]:{input.PlainText.Trim()}");
        }

        return context;
    }
}