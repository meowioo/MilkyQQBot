using System.Text;
using MilkyQQBot.Features.ChatAi.V2.Models;

namespace MilkyQQBot.Features.ChatAi.V2.Persona;

/// <summary>
/// 人格 Prompt 构造器。
/// 把人格、当前场景、行为要求，拼成一个更稳定的 system prompt。
/// </summary>
public static class PersonaPromptBuilder
{
    /// <summary>
    /// 构建本次对话使用的 system prompt。
    /// </summary>
    public static string Build(
        PersonaProfile persona,
        ChatAiInput input,
        GroupConversationState conv,
        string triggerReason)
    {
        var sb = new StringBuilder();

        sb.AppendLine(persona.RoleDescription);
        sb.AppendLine(persona.SpeakingStyle);
        sb.AppendLine(persona.Constraints);

        sb.AppendLine("你现在在QQ群聊天。你的目标不是帮助用户完成任务，而是自然地接一句群聊。");
        sb.AppendLine("回复要像真人群友，而不是AI助手。");
        sb.AppendLine("回复尽量短，通常控制在 4 到 20 个字。");
        sb.AppendLine("只有在很有必要时才说长一点，但也不要超过两句。");
        sb.AppendLine("不要解释自己，不要说教，不要自称人工智能。");
        sb.AppendLine("不要重复之前刚说过的内容。");
        sb.AppendLine("不要把图片、表情、回复标记原封不动复述出来，应该像看懂了群聊后自然接话。");

        if (input.IsBotMentioned)
        {
            sb.AppendLine("当前这条消息明确 @ 了你，所以要更直接回应。");
        }

        if (conv.BotEngaged)
        {
            sb.AppendLine("你已经参与了当前这轮话题，所以可以自然跟进一句，但不要刷屏。");
        }

        sb.AppendLine($"本次触发原因：{triggerReason}");

        if (persona.BannedPhrases.Count > 0)
        {
            sb.AppendLine("禁止使用这些表达：");
            foreach (var phrase in persona.BannedPhrases)
            {
                sb.AppendLine($"- {phrase}");
            }
        }

        return sb.ToString().Trim();
    }
}