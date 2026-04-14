using MilkyQQBot.Features.ChatAi.V2.Models;
using MilkyQQBot.Services;

namespace MilkyQQBot.Features.ChatAi.V2;

public static class V2Trigger
{
    public static V2TriggerDecision Evaluate(
        ChatAiInput input,
        BotRuntimeState state,
        GroupConversationState conv,
        List<string> recentMessages)
    {
        if (input.IsBotMentioned)
        {
            return V2TriggerDecision.Trigger("被@必回");
        }

        if (state.GroupAiLastReplyTime.TryGetValue(input.GroupId, out DateTime lastReplyTime))
        {
            if ((DateTime.Now - lastReplyTime).TotalSeconds < 15)
            {
                return V2TriggerDecision.Skip("15秒内已回复过");
            }
        }

        // 正常群聊触发：至少2个参与者，至少3条人类消息
        if (!conv.BotEngaged &&
            conv.RecentParticipantIds.Count >= 2 &&
            conv.RecentHumanMessageCount >= 3)
        {
            return V2TriggerDecision.Trigger("新话题形成");
        }

        // 单人测试兜底：同一个人连续发了3条以上，也允许触发一次
        if (!conv.BotEngaged &&
            conv.RecentParticipantIds.Count == 1 &&
            conv.RecentHumanMessageCount >= 3)
        {
            return V2TriggerDecision.Trigger("单人连续发言测试");
        }

        // 机器人已经参与这轮讨论后，允许跟进一次
        if (conv.BotEngaged &&
            conv.HumanMessagesSinceBotReply >= 2 &&
            (DateTime.Now - conv.LastHumanMessageAt).TotalSeconds <= 45)
        {
            return V2TriggerDecision.Trigger("跟进当前话题");
        }

        return V2TriggerDecision.Skip("讨论热度不足");
    }
}

public sealed class V2TriggerDecision
{
    public bool ShouldTrigger { get; init; }
    public string Reason { get; init; } = "";

    public static V2TriggerDecision Trigger(string reason) =>
        new() { ShouldTrigger = true, Reason = reason };

    public static V2TriggerDecision Skip(string reason) =>
        new() { ShouldTrigger = false, Reason = reason };
}