using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Milky.Net.Client;
using Milky.Net.Model;
using MilkyQQBot.Features.ChatAi;
using MilkyQQBot.Features.ChatAi.V2.Models;

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

                await ChatAiEntry.HandleGroupMessageAsync(milky, state, new ChatAiInput
                {
                    GroupId = peerId,
                    SenderId = senderId,
                    SenderNickname = senderNickname,
                    PlainText = pureTextBuilder.ToString(),
                    IsBotMentioned = isBotMentioned,
                    BotId = myBotId
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析消息失败: {ex.Message}");
        }
    }
}