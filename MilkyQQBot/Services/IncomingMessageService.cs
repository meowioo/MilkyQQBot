using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot.Services;

public static class IncomingMessageService
{
    public static async Task HandleAsync(
        MilkyClient milky,
        CommandHandler commandHandler,
        BotRuntimeState state,
        IncomingMessage incomingMessage)
    {
        try
        {
            string scene;
            long senderId;
            long peerId;
            IEnumerable<IncomingSegment> segments;
            string logPrefix;
            long messageSeq = 0;
            string senderNickname = "";
            string role = "Member";

            long myBotId = AppConfig.Current.Bot.BotId;
            bool isBotMentioned = false;

            switch (incomingMessage)
            {
                case GroupIncomingMessage groupMsg:
                    scene = "group";
                    senderId = groupMsg.SenderId;
                    peerId = groupMsg.PeerId;
                    segments = groupMsg.Segments;
                    messageSeq = groupMsg.MessageSeq;

                    var groupName = groupMsg.Group?.GroupName ?? "未知群聊";
                    senderNickname = groupMsg.GroupMember?.Nickname ?? "未知用户";
                    role = groupMsg.GroupMember?.Role.ToString() ?? "Member";

                    logPrefix = $"[群聊] {groupName}({peerId}) 用户[{senderNickname}]({senderId}) 说:";
                    break;

                case FriendIncomingMessage privateMsg:
                    scene = "friend";
                    senderId = privateMsg.SenderId;
                    peerId = privateMsg.PeerId;
                    segments = privateMsg.Segments;
                    logPrefix = $"[私聊] 好友({senderId}) 说:";
                    break;

                default:
                    return;
            }

            var fullMessageBuilder = new StringBuilder();
            var pureTextBuilder = new StringBuilder();
            var simplifiedSegments = new List<object>();

            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case IncomingSegment<TextIncomingSegmentData> textSeg:
                    {
                        var text = textSeg.Data?.Text ?? "";
                        fullMessageBuilder.Append(text);
                        pureTextBuilder.Append(text);
                        simplifiedSegments.Add(new { type = "text", data = new { text } });
                        break;
                    }
                    case IncomingSegment<ImageIncomingSegmentData> imgSeg:
                        fullMessageBuilder.Append("[图片]");
                        simplifiedSegments.Add(new { type = "image", data = new { url = imgSeg.Data.TempUrl } });
                        break;

                    case IncomingSegment<FaceIncomingSegmentData> faceSeg:
                        fullMessageBuilder.Append("[表情]");
                        simplifiedSegments.Add(new { type = "face", data = new { id = faceSeg.Data.FaceId?.ToString() } });
                        break;

                    case IncomingSegment<MentionIncomingSegmentData> mentionSeg:
                        long targetId = mentionSeg.Data?.UserId ?? 0;
                        fullMessageBuilder.Append($"[@{targetId}]");
                        simplifiedSegments.Add(new { type = "mention", data = new { user_id = targetId } });

                        if (targetId == myBotId)
                            isBotMentioned = true;
                        break;

                    case IncomingSegment<MentionAllIncomingSegmentData>:
                        fullMessageBuilder.Append("[@全体成员]");
                        simplifiedSegments.Add(new { type = "mention_all", data = new { } });
                        break;

                    case IncomingSegment<ReplyIncomingSegmentData> replySeg:
                        long seq = replySeg.Data?.MessageSeq ?? 0;
                        fullMessageBuilder.Append("[回复]");
                        simplifiedSegments.Add(new { type = "reply", data = new { message_seq = seq } });
                        break;
                }
            }

            string fullMessage = fullMessageBuilder.ToString();
            Console.WriteLine($"{logPrefix} {fullMessage}");

            if (fullMessage.TrimStart().StartsWith("/") && senderId != myBotId)
            {
                var context = new CommandContext
                {
                    Client = milky,
                    Scene = scene,
                    SenderId = senderId,
                    PeerId = peerId,
                    Command = fullMessage.TrimStart(),
                    SenderRole = role,
                    MessageSeq = messageSeq
                };

                await commandHandler.ExecuteAsync(context);
            }

            if (scene == "group")
            {
                DatabaseManager.SaveGroupMessage(
                    messageSeq: messageSeq,
                    groupId: peerId,
                    senderId: senderId,
                    nickname: senderNickname,
                    plainText: fullMessage,
                    segments: simplifiedSegments
                );

                await TryTriggerGroupAiAsync(
                    milky,
                    state,
                    peerId,
                    senderId,
                    senderNickname,
                    pureTextBuilder.ToString(),
                    isBotMentioned,
                    myBotId
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析消息失败: {ex.Message}");
        }
    }

    private static async Task TryTriggerGroupAiAsync(
        MilkyClient milky,
        BotRuntimeState state,
        long peerId,
        long senderId,
        string senderNickname,
        string pureText,
        bool isBotMentioned,
        long myBotId)
    {
        var config = GroupConfigManager.GetConfig(peerId);

        string pureTextForAi = Regex.Replace(pureText, @"https?://[^\s]+", "").Trim();

        if (!config.AiChatEnabled ||
            pureTextForAi.StartsWith("/") ||
            string.IsNullOrWhiteSpace(pureTextForAi) ||
            senderId == myBotId)
        {
            return;
        }

        bool shouldTrigger = false;

        if (peerId == 792316113)
        {
            shouldTrigger = true;
        }
        else if (isBotMentioned)
        {
            shouldTrigger = true;
        }
        else if (Random.Shared.Next(100) < 30)
        {
            shouldTrigger = true;
        }

        if (!shouldTrigger) return;

        if (state.GroupAiThinkingStatus.TryGetValue(peerId, out bool isThinking) && isThinking)
        {
            Console.WriteLine($"[AI拦截] 群 {peerId} 机器人正在思考中，忽略本次触发...");
            return;
        }

        int cooldownSeconds = isBotMentioned ? 5 : 10;
        if (state.GroupAiLastReplyTime.TryGetValue(peerId, out DateTime lastReplyTime))
        {
            if ((DateTime.Now - lastReplyTime).TotalSeconds < cooldownSeconds)
            {
                Console.WriteLine($"[AI拦截] 群 {peerId} 处于 {cooldownSeconds}s 冷却期内，忽略触发...");
                return;
            }
        }

        state.GroupAiThinkingStatus[peerId] = true;

        _ = Task.Run(async () =>
        {
            try
            {
                string reason = isBotMentioned ? "被@特权" : (peerId == 792316113 ? "测试群特权" : "30%概率");
                Console.WriteLine($"[AI触发] 群 {peerId} 满足条件，原因：{reason}");

                List<string> history;
                if (isBotMentioned)
                {
                    history = new List<string> { $"[{senderId}][{senderNickname}]:{pureTextForAi}" };
                }
                else
                {
                    history = DatabaseManager.GetRecentGroupMessagesFormatted(peerId, 50);
                }

                string aiReply = await AiChatService.GetAiResponseAsync(history);

                if (!string.IsNullOrWhiteSpace(aiReply))
                {
                    aiReply = aiReply.Trim();
                    var segment = new OutgoingSegment<TextOutgoingSegmentData>(new(aiReply));
                    var req = new SendGroupMessageRequest(peerId, new[] { segment });
                    await milky.Message.SendGroupMessageAsync(req);

                    Console.WriteLine($"[AI回复] {aiReply}");
                    state.GroupAiLastReplyTime[peerId] = DateTime.Now;
                }
            }
            catch (Exception aiEx)
            {
                Console.WriteLine($"[AI执行异常] {aiEx.Message}");
            }
            finally
            {
                state.GroupAiThinkingStatus[peerId] = false;
            }
        });

        await Task.CompletedTask;
    }
}