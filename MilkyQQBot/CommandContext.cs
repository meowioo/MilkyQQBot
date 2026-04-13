using System.Linq;
using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot;

public class CommandContext
{
    public MilkyClient Client { get; set; }
    public string Scene { get; set; } = "";
    public long SenderId { get; set; }
    public long PeerId { get; set; }
    public string Command { get; set; } = "";
    public string[] Args { get; set; } = Array.Empty<string>();
    public string SenderRole { get; set; } = "";
    public long MessageSeq { get; set; }

    public bool IsGroup => string.Equals(Scene, "group", StringComparison.OrdinalIgnoreCase);
    public bool IsFriend => string.Equals(Scene, "friend", StringComparison.OrdinalIgnoreCase);

    public static CommandContext CreateGroup(MilkyClient client, long groupId, long messageSeq = 0)
    {
        return new CommandContext
        {
            Client = client,
            Scene = "group",
            PeerId = groupId,
            MessageSeq = messageSeq,
            Args = Array.Empty<string>()
        };
    }

    public static CommandContext CreateFriend(MilkyClient client, long userId, long messageSeq = 0)
    {
        return new CommandContext
        {
            Client = client,
            Scene = "friend",
            PeerId = userId,
            MessageSeq = messageSeq,
            Args = Array.Empty<string>()
        };
    }

    private void EnsureValid()
    {
        if (Client == null)
            throw new InvalidOperationException("CommandContext.Client 不能为空。");

        if (!IsGroup && !IsFriend)
            throw new InvalidOperationException($"未知的消息场景: {Scene}");
    }

    private async Task SendCoreAsync(params OutgoingSegment[] segments)
    {
        EnsureValid();

        if (segments == null || segments.Length == 0)
            return;

        if (IsGroup)
        {
            var req = new SendGroupMessageRequest(PeerId, segments);
            await Client.Message.SendGroupMessageAsync(req);
            Console.WriteLine($"[消息已发送] -> 群聊:{PeerId}");
            return;
        }

        var privateReq = new SendPrivateMessageRequest(PeerId, segments);
        await Client.Message.SendPrivateMessageAsync(privateReq);
        Console.WriteLine($"[消息已发送] -> 私聊:{PeerId}");
    }

    private static void LogSendError(string action, Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{action}发送失败] {ex.Message}");
        Console.ResetColor();
    }

    public async Task SendAsync(params OutgoingSegment[] segments)
    {
        try
        {
            await SendCoreAsync(segments);
        }
        catch (Exception ex)
        {
            LogSendError("消息", ex);
        }
    }

    public Task TextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        return SendAsync(Seg.Text(text));
    }

    public Task LinesAsync(params string[] lines)
    {
        if (lines == null || lines.Length == 0)
            return Task.CompletedTask;

        var text = string.Join(
            Environment.NewLine,
            lines.Where(x => !string.IsNullOrWhiteSpace(x))
        );

        return TextAsync(text);
    }

    public Task ImageAsync(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
            return Task.CompletedTask;

        return SendAsync(Seg.Image(imageUrl));
    }
    

    public async Task VideoAsync(string videoUrl, string? thumbUrl = null)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        try
        {
            await SendCoreAsync(Seg.Video(videoUrl, thumbUrl));
        }
        catch (Exception ex)
        {
            LogSendError("视频", ex);
            await TextAsync($"[视频链接]\n{videoUrl}");
        }
    }

    public Task AtAsync(long userId, string text)
    {
        return SendAsync(
            Seg.Mention(userId),
            Seg.Text(string.IsNullOrWhiteSpace(text) ? "" : " " + text)
        );
    }

    public Task ReplyToMessageAsync(string text)
    {
        if (MessageSeq <= 0)
            return TextAsync(text);

        return SendAsync(
            Seg.Reply(MessageSeq),
            Seg.Text(text)
        );
    }

    public Task ReplyToMessageAsync(params OutgoingSegment[] segments)
    {
        if (segments == null || segments.Length == 0)
            return Task.CompletedTask;

        if (MessageSeq <= 0)
            return SendAsync(segments);

        var finalSegments = new OutgoingSegment[segments.Length + 1];
        finalSegments[0] = Seg.Reply(MessageSeq);
        Array.Copy(segments, 0, finalSegments, 1, segments.Length);

        return SendAsync(finalSegments);
    }

    public static class Seg
    {
        public static OutgoingSegment Text(string text)
        {
            return new OutgoingSegment<TextOutgoingSegmentData>(
                new TextOutgoingSegmentData(text ?? string.Empty)
            );
        }

        public static OutgoingSegment Image(string imageUrl)
        {
            return new OutgoingSegment<ImageOutgoingSegmentData>(
                new ImageOutgoingSegmentData(new MilkyUri(imageUrl), null)
            );
        }

        public static OutgoingSegment Video(string videoUrl, string? thumbUrl = null)
        {
            return new OutgoingSegment<VideoOutgoingSegmentData>(
                new VideoOutgoingSegmentData(
                    new MilkyUri(videoUrl),
                    string.IsNullOrWhiteSpace(thumbUrl) ? null : new MilkyUri(thumbUrl)
                )
            );
        }

        public static OutgoingSegment Mention(long userId)
        {
            return new OutgoingSegment<MentionOutgoingSegmentData>(
                new MentionOutgoingSegmentData(userId)
            );
        }

        public static OutgoingSegment Reply(long messageSeq)
        {
            return new OutgoingSegment<ReplyOutgoingSegmentData>(
                new ReplyOutgoingSegmentData(messageSeq)
            );
        }
    }
}