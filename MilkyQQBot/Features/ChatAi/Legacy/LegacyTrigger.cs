using MilkyQQBot.Features.ChatAi.V2.Models;

namespace MilkyQQBot.Features.ChatAi.Legacy;

public static class LegacyTrigger
{
    public static LegacyTriggerDecision Evaluate(ChatAiInput input)
    {
        if (input.GroupId == 792316113)
        {
            return LegacyTriggerDecision.Trigger("测试群特权");
        }

        if (input.IsBotMentioned)
        {
            return LegacyTriggerDecision.Trigger("被@特权");
        }

        if (Random.Shared.Next(100) < 30)
        {
            return LegacyTriggerDecision.Trigger("30%概率");
        }

        return LegacyTriggerDecision.Skip("未命中触发条件");
    }
}

public sealed class LegacyTriggerDecision
{
    public bool ShouldTrigger { get; init; }
    public string Reason { get; init; } = "";

    public static LegacyTriggerDecision Trigger(string reason)
    {
        return new LegacyTriggerDecision
        {
            ShouldTrigger = true,
            Reason = reason
        };
    }

    public static LegacyTriggerDecision Skip(string reason)
    {
        return new LegacyTriggerDecision
        {
            ShouldTrigger = false,
            Reason = reason
        };
    }
}