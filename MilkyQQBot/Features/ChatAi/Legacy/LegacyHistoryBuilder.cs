using MilkyQQBot.Features.ChatAi.V2.Models;
using MilkyQQBot.Services;

namespace MilkyQQBot.Features.ChatAi.Legacy;

public static class LegacyHistoryBuilder
{
    public static List<string> Build(ChatAiInput input, string pureTextForAi)
    {
        if (input.IsBotMentioned)
        {
            return new List<string>
            {
                $"[{input.SenderId}][{input.SenderNickname}]:{pureTextForAi}"
            };
        }

        return DatabaseManager.GetRecentGroupMessagesFormatted(input.GroupId, 50);
    }
}