using System.Text.RegularExpressions;

namespace MilkyQQBot.Features.ChatAi.V2;

public static class ResponseStyleGuard
{
    public static StyleGuardResult Evaluate(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return StyleGuardResult.Block("空回复");

        string text = reply.Trim();

        if (text.Length > 45)
            return StyleGuardResult.Block("回复过长");

        string[] bannedPhrases =
        {
            "首先",
            "其次",
            "最后",
            "总的来说",
            "总之",
            "根据你提供的信息",
            "从这个角度来看",
            "我认为可以",
            "以下是",
            "建议你",
            "需要注意的是"
        };

        foreach (var phrase in bannedPhrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return StyleGuardResult.Block($"命中书面语短语: {phrase}");
            }
        }

        if (Regex.IsMatch(text, @"[1-9]\.|一、|二、|三、"))
            return StyleGuardResult.Block("命中分点表达");

        if (text.Count(c => c == '，' || c == '。' || c == '；') >= 4)
            return StyleGuardResult.Block("句子过于书面化");

        return StyleGuardResult.Pass();
    }
}

public sealed class StyleGuardResult
{
    public bool ShouldBlock { get; init; }
    public string Reason { get; init; } = "";

    public static StyleGuardResult Pass() =>
        new() { ShouldBlock = false };

    public static StyleGuardResult Block(string reason) =>
        new() { ShouldBlock = true, Reason = reason };
}