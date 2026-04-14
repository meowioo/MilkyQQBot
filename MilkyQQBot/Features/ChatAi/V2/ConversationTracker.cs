using MilkyQQBot.Features.ChatAi.V2.Models;
using MilkyQQBot.Services;

namespace MilkyQQBot.Features.ChatAi.V2;

public static class ConversationTracker
{
    public static GroupConversationState Observe(
        BotRuntimeState state,
        ChatAiInput input)
    {
        if (!state.GroupConversationStates.TryGetValue(input.GroupId, out var conv))
        {
            conv = new GroupConversationState
            {
                GroupId = input.GroupId
            };
            state.GroupConversationStates[input.GroupId] = conv;
        }

        var now = DateTime.Now;

        // 超过90秒没人说话，视为新一轮讨论
        if ((now - conv.LastMessageAt).TotalSeconds > 90)
        {
            conv.RecentHumanMessageCount = 0;
            conv.RecentParticipantIds.Clear();
            conv.BotEngaged = false;
            conv.BotEngagedAt = null;
            conv.HumanMessagesSinceBotReply = 0;
            conv.LastTriggerReason = "";
        }

        conv.LastMessageAt = now;

        if (input.SenderId != input.BotId && !string.IsNullOrWhiteSpace(input.PlainText))
        {
            conv.LastHumanMessageAt = now;
            conv.RecentHumanMessageCount++;
            conv.RecentParticipantIds.Add(input.SenderId);
            conv.HumanMessagesSinceBotReply++;
        }

        return conv;
    }

    public static void MarkBotReplied(
        BotRuntimeState state,
        long groupId,
        string reason)
    {
        if (!state.GroupConversationStates.TryGetValue(groupId, out var conv))
            return;

        conv.BotEngaged = true;
        conv.BotEngagedAt = DateTime.Now;
        conv.HumanMessagesSinceBotReply = 0;
        conv.LastTriggerReason = reason;
    }
}