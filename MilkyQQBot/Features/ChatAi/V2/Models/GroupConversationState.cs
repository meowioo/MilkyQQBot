namespace MilkyQQBot.Features.ChatAi.V2.Models;

public sealed class GroupConversationState
{
    public long GroupId { get; set; }

    public DateTime LastMessageAt { get; set; }
    public DateTime LastHumanMessageAt { get; set; }

    public int RecentHumanMessageCount { get; set; }
    public HashSet<long> RecentParticipantIds { get; set; } = new();

    public bool BotEngaged { get; set; }
    public DateTime? BotEngagedAt { get; set; }

    public int HumanMessagesSinceBotReply { get; set; }

    public string LastTriggerReason { get; set; } = "";
}