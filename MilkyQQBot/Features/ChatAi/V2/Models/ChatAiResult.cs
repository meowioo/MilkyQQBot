namespace MilkyQQBot.Features.ChatAi.V2.Models;

public sealed class ChatAiResult
{
    public bool ShouldReply { get; init; }
    public string? ReplyText { get; init; }
    public string Reason { get; init; } = "";
}