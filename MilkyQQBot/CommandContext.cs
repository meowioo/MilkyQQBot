using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot;

public class CommandContext
{
    public MilkyClient Client { get; set; }    // 传入客户端，方便在指令中调用发消息的 API
    public string Scene { get; set; }        // "group" 或 "friend"
    public long SenderId { get; set; }      // 发送者 QQ
    public long PeerId { get; set; }        // 目标 ID（群号或好友QQ）
    public string Command { get; set; }     // 触发的指令，如 "/epic"
    public string[] Args { get; set; }     // 指令参数，比如 "/roll 100" -> Args[0] 就是 "100"
    public string SenderRole { get; set; } //发送者在群里的角色权限
    
    
    private async Task SendSegmentsAsync(params OutgoingSegment[] segments)
    {
        if (Scene == "group")
        {
            var req = new SendGroupMessageRequest(PeerId, segments);
            await Client.Message.SendGroupMessageAsync(req);
            Console.WriteLine($"[消息已发送] -> 群聊:{PeerId}");
        }
        else if (Scene == "friend")
        {
            var req = new SendPrivateMessageRequest(PeerId, segments);
            await Client.Message.SendPrivateMessageAsync(req);
            Console.WriteLine($"[消息已发送] -> 私聊:{PeerId}");
        }
    }
    
    // ==========================================
    // 【核心魔法】封装一个极其方便的快捷回复方法
    // ==========================================
    public async Task ReplyAsync(string text)
    {
        try
        {
            var segment = new OutgoingSegment<TextOutgoingSegmentData>(new TextOutgoingSegmentData(text));
            await SendSegmentsAsync(segment);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[文本发送失败] {ex.Message}");
            Console.ResetColor();
        }
    }
    
    // ==========================================
    // 【新增】发送图片的方法
    // ==========================================
    public async Task ReplyImageAsync(string imageUrl)
    {
        try
        {
            // 只负责组装 Image 类型的 Segment
            var segment = new OutgoingSegment<ImageOutgoingSegmentData>(new ImageOutgoingSegmentData(new MilkyUri(imageUrl), null));
            // 同样，把发送逻辑外包出去
            await SendSegmentsAsync(segment);
            Console.WriteLine($"[图片已发送] -> {Scene}:{PeerId}");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[图片发送失败] {ex.Message}");
            Console.ResetColor();
        }
    }
}