using System.Text.RegularExpressions;
using Milky.Net.Client;
using Milky.Net.Model;
using MilkyQQBot.Features.ChatAi.Legacy;
using MilkyQQBot.Features.ChatAi.V2;
using MilkyQQBot.Features.ChatAi.V2.Models;
using MilkyQQBot.Services;

namespace MilkyQQBot.Features.ChatAi;

public static class ChatAiEntry
{
    public static async Task HandleGroupMessageAsync(
        MilkyClient milky,
        BotRuntimeState state,
        ChatAiInput input)
    {
        GroupFeatureConfig config = GroupConfigManager.GetConfig(input.GroupId);
        string pureTextForAi = NormalizeTextForAi(input.PlainText);

        if (ShouldSkip(config, input, pureTextForAi))
            return;
                
        var conv = ConversationTracker.Observe(state, input);
        List<string> recentMessages = DatabaseManager.GetRecentGroupMessagesFormatted(input.GroupId, 3);
        var triggerDecision = V2Trigger.Evaluate(input, state, conv, recentMessages);

        if (!triggerDecision.ShouldTrigger)
            return;

        if (!TryEnterThinking(state, input.GroupId, input.IsBotMentioned, out _))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteReplyAsync(
                    milky,
                    state,
                    input,
                    pureTextForAi,
                    triggerDecision.Reason);
            }
            catch (Exception aiEx)
            {
                Console.WriteLine($"[AI执行异常] {aiEx.Message}");
            }
            finally
            {
                state.GroupAiThinkingStatus[input.GroupId] = false;
            }
        });

        await Task.CompletedTask;
    }

    private static string NormalizeTextForAi(string text)
    {
        return Regex.Replace(text, @"https?://[^\s]+", "").Trim();
    }

    private static bool ShouldSkip(
        GroupFeatureConfig config,
        ChatAiInput input,
        string pureTextForAi)
    {
        return !config.AiChatEnabled ||
               pureTextForAi.StartsWith("/") ||
               string.IsNullOrWhiteSpace(pureTextForAi) ||
               input.SenderId == input.BotId;
    }

    private static bool TryEnterThinking(
        BotRuntimeState state,
        long groupId,
        bool isBotMentioned,
        out int cooldownSeconds)
    {
        if (state.GroupAiThinkingStatus.TryGetValue(groupId, out bool isThinking) && isThinking)
        {
            Console.WriteLine($"[AI拦截] 群 {groupId} 机器人正在思考中，忽略本次触发...");
            cooldownSeconds = 0;
            return false;
        }

        cooldownSeconds = isBotMentioned ? 5 : 10;

        if (state.GroupAiLastReplyTime.TryGetValue(groupId, out DateTime lastReplyTime))
        {
            if ((DateTime.Now - lastReplyTime).TotalSeconds < cooldownSeconds)
            {
                Console.WriteLine($"[AI拦截] 群 {groupId} 处于 {cooldownSeconds}s 冷却期内，忽略触发...");
                return false;
            }
        }

        state.GroupAiThinkingStatus[groupId] = true;
        return true;
    }

    private static async Task ExecuteReplyAsync(
        MilkyClient milky,
        BotRuntimeState state,
        ChatAiInput input,
        string pureTextForAi,
        string triggerReason)
    {
        Console.WriteLine($"[AI触发] 群 {input.GroupId} 满足条件，原因：{triggerReason}");

        List<string> history = LegacyHistoryBuilder.Build(input, pureTextForAi);
        string aiReply = await AiChatService.GetAiResponseAsync(history);

        if (string.IsNullOrWhiteSpace(aiReply))
            return;

        aiReply = aiReply.Trim();
        await SendReplyAsync(milky, input.GroupId, aiReply);

        Console.WriteLine($"[AI回复] {aiReply}");
        state.GroupAiLastReplyTime[input.GroupId] = DateTime.Now;
        ConversationTracker.MarkBotReplied(state, input.GroupId, triggerReason);
    }

    private static async Task SendReplyAsync(
        MilkyClient milky,
        long groupId,
        string replyText)
    {
        var segment = new OutgoingSegment<TextOutgoingSegmentData>(new(replyText));
        var req = new SendGroupMessageRequest(groupId, new[] { segment });
        await milky.Message.SendGroupMessageAsync(req);
    }
}