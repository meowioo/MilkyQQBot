using System.Text.RegularExpressions;

namespace MilkyQQBot.Features.ChatAi.V2;

public static class SelfRepeatGuard
{
    public static bool IsTooSimilar(string candidate, List<string> recentBotReplies)
    {
        string normalizedCandidate = Normalize(candidate);

        if (string.IsNullOrWhiteSpace(normalizedCandidate))
            return true;

        foreach (var reply in recentBotReplies)
        {
            string normalizedReply = Normalize(reply);

            if (string.IsNullOrWhiteSpace(normalizedReply))
                continue;

            if (normalizedCandidate == normalizedReply)
                return true;

            if (normalizedCandidate.Contains(normalizedReply) || normalizedReply.Contains(normalizedCandidate))
                return true;

            double similarity = CalculateSimilarity(normalizedCandidate, normalizedReply);
            if (similarity >= 0.85)
                return true;
        }

        return false;
    }

    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant().Trim();
        text = Regex.Replace(text, @"\s+", "");
        text = Regex.Replace(text, @"[^\p{L}\p{N}\u4e00-\u9fff]", "");
        return text;
    }

    private static double CalculateSimilarity(string a, string b)
    {
        int distance = LevenshteinDistance(a, b);
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        return 1.0 - (double)distance / maxLen;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] dp = new int[a.Length + 1, b.Length + 1];

        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}