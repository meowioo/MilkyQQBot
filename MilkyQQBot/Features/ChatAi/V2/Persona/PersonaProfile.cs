namespace MilkyQQBot.Features.ChatAi.V2.Persona;

/// <summary>
/// 人格配置对象。
/// 第一版先做最小可用：名字、身份、说话风格、禁用表达。
/// </summary>
public sealed class PersonaProfile
{
    /// <summary>
    /// 人格名称，仅用于调试日志。
    /// </summary>
    public string Name { get; init; } = "default";

    /// <summary>
    /// 机器人在群里的身份设定。
    /// 例如：嘴贫群友 / 冷淡吐槽役 / 活跃气氛组。
    /// </summary>
    public string RoleDescription { get; init; } = "";

    /// <summary>
    /// 说话风格描述。
    /// 例如：简短、口语、带一点损、不要太正式。
    /// </summary>
    public string SpeakingStyle { get; init; } = "";

    /// <summary>
    /// 行为边界描述。
    /// 例如：不要长篇解释、不要像客服、不要总是教育别人。
    /// </summary>
    public string Constraints { get; init; } = "";

    /// <summary>
    /// 禁止使用的词/短语。
    /// </summary>
    public IReadOnlyList<string> BannedPhrases { get; init; } = Array.Empty<string>();
}