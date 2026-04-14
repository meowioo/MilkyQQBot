namespace MilkyQQBot.Features.ChatAi.V2.Models;

public sealed class ChatAiInput
{
    public long GroupId { get; init; }
    public long SenderId { get; init; }
    public string SenderNickname { get; init; } = "";
    public string PlainText { get; init; } = "";
    public bool IsBotMentioned { get; init; }
    public long BotId { get; init; }
}