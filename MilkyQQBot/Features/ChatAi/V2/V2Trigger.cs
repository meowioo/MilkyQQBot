using MilkyQQBot.Features.ChatAi.V2.Models;
using MilkyQQBot.Services;

namespace MilkyQQBot.Features.ChatAi.V2;

public static class V2Trigger
{
    public static V2TriggerDecision Evaluate(
        ChatAiInput input,
        BotRuntimeState state,
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

        int nonEmptyCount = recentMessages.Count(x => !string.IsNullOrWhiteSpace(x));
        if (nonEmptyCount >= 2)
        {
            return V2TriggerDecision.Trigger("最近3条消息形成讨论");
        }

        return V2TriggerDecision.Skip("未形成讨论");
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