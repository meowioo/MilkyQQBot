using System;
using System.Collections.Generic;
using System.Text.Json;
using Milky.Net.Client;
using Milky.Net.Model;
using MilkyQQBot.Services;

namespace MilkyQQBot.Events;

public static class BotEventRegistrar
{
    public static void Register(MilkyClient milky, CommandHandler commandHandler, BotRuntimeState state)
    {
        milky.Events.BotOffline += (sender, e) =>
        {
            Console.WriteLine("机器人已离线！");
        };

        milky.Events.MessageRecall += async (sender, e) =>
        {
            try
            {
                if (e.Data.MessageScene is MessageScene.Group)
                {
                    long groupId = e.Data.PeerId;
                    long messageSeq = e.Data.MessageSeq;

                    // senderId: 被撤回消息原本的发送者
                    // operatorId: 实际执行撤回操作的人
                    long senderId = e.Data.SenderId;
                    long operatorId = e.Data.OperatorId;

                    var config = GroupConfigManager.GetConfig(groupId);
                    if (!config.AntiRecallEnabled) return;

                    // 只处理“发送者自己撤回自己消息”的情况。
                    // 如果 senderId != operatorId，说明这是管理员/群主代撤，
                    // 这种情况不需要防撤回，直接跳过。
                    //
                    // 注意：
                    // 管理员自己撤回自己的消息时，senderId == operatorId，
                    // 仍然会继续走下面的防撤回逻辑，符合你的需求。
                    if (senderId != operatorId)
                    {
                        Console.WriteLine(
                            $"[防撤回跳过] 群 {groupId} 中消息 {messageSeq} 为管理员代撤，发送者={senderId}，操作者={operatorId}"
                        );
                        return;
                    }

                    var msgData = DatabaseManager.GetMessageBySeq(groupId, messageSeq);

                    if (!string.IsNullOrEmpty(msgData.Nickname))
                    {
                        var outgoingSegments = new List<OutgoingSegment>();
                        string alertText = $"已拦截到 [{msgData.Nickname}] 撤回的消息：\n";
                        outgoingSegments.Add(new OutgoingSegment<TextOutgoingSegmentData>(new(alertText)));

                        try
                        {
                            using var doc = JsonDocument.Parse(msgData.RawSegmentsJson);

                            foreach (var element in doc.RootElement.EnumerateArray())
                            {
                                string type = element.TryGetProperty("Type", out var typeProp)
                                    ? typeProp.GetString()?.ToLower()
                                    : element.TryGetProperty("type", out typeProp)
                                        ? typeProp.GetString()?.ToLower()
                                        : "";

                                JsonElement dataProp;
                                bool hasData = element.TryGetProperty("Data", out dataProp) ||
                                               element.TryGetProperty("data", out dataProp);

                                if (!hasData) continue;

                                if (type == "text")
                                {
                                    string text = dataProp.TryGetProperty("Text", out var tProp)
                                        ? tProp.GetString()
                                        : dataProp.TryGetProperty("text", out tProp)
                                            ? tProp.GetString()
                                            : "";

                                    outgoingSegments.Add(new OutgoingSegment<TextOutgoingSegmentData>(new(text)));
                                }
                                else if (type == "image")
                                {
                                    string url = dataProp.TryGetProperty("Url", out var uProp)
                                        ? uProp.GetString()
                                        : dataProp.TryGetProperty("url", out uProp)
                                            ? uProp.GetString()
                                            : "";

                                    if (!string.IsNullOrEmpty(url))
                                    {
                                        outgoingSegments.Add(new OutgoingSegment<ImageOutgoingSegmentData>(new(new(url), null)));
                                    }
                                }
                                else if (type == "face")
                                {
                                    string faceIdStr = dataProp.TryGetProperty("Id", out var idProp)
                                        ? idProp.ToString()
                                        : dataProp.TryGetProperty("id", out idProp)
                                            ? idProp.ToString()
                                            : "";

                                    if (!string.IsNullOrEmpty(faceIdStr))
                                    {
                                        outgoingSegments.Add(new OutgoingSegment<FaceOutgoingSegmentData>(new(faceIdStr)));
                                    }
                                }
                                else if (type == "mention")
                                {
                                    string userIdStr = dataProp.TryGetProperty("user_id", out var uidProp)
                                        ? uidProp.ToString()
                                        : dataProp.TryGetProperty("UserId", out uidProp)
                                            ? uidProp.ToString()
                                            : "0";

                                    if (long.TryParse(userIdStr, out long uid))
                                    {
                                        outgoingSegments.Add(new OutgoingSegment<MentionOutgoingSegmentData>(new(uid)));
                                    }
                                }
                                else if (type == "mention_all")
                                {
                                    outgoingSegments.Add(new OutgoingSegment<MentionAllOutgoingSegmentData>(new()));
                                }
                                else if (type == "reply")
                                {
                                    string seqStr = dataProp.TryGetProperty("message_seq", out var seqProp)
                                        ? seqProp.ToString()
                                        : dataProp.TryGetProperty("MessageSeq", out seqProp)
                                            ? seqProp.ToString()
                                            : "0";

                                    if (long.TryParse(seqStr, out long seq))
                                    {
                                        outgoingSegments.Add(new OutgoingSegment<ReplyOutgoingSegmentData>(new(seq)));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[JSON还原图文失败] {ex.Message}，回退到纯文本显示。");
                            outgoingSegments.Add(new OutgoingSegment<TextOutgoingSegmentData>(new(msgData.PlainText)));
                        }

                        var req = new SendGroupMessageRequest(groupId, outgoingSegments.ToArray());
                        await milky.Message.SendGroupMessageAsync(req);

                        Console.WriteLine($"[防撤回触发] 已在群 {groupId} 公开撤回的真实内容");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[撤回事件解析失败] {ex.Message}");
            }
        };

        milky.Events.MessageReceive += async (sender, e) =>
        {
            await IncomingMessageService.HandleAsync(milky, commandHandler, state, e.Data);
        };
    }
}