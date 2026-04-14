using MilkyQQBot.Features.ChatAi.V2.Models;

namespace MilkyQQBot.Features.ChatAi;

public interface IChatAiEngine
{
    Task<ChatAiResult> ProcessAsync(ChatAiInput input);
}