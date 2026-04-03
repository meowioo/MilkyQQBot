using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MilkyQQBot;

public static class SteamIdResolverService
{
    private static readonly HttpClient _httpClient = new();

    private static string ApiKey => AppConfig.Current.Steam.ApiKey;

    public static async Task<string> ResolveSteamIdAsync(string input)
    {
        input = input.Trim();

        // 1. 如果群友老老实实输入了 17 位纯数字，直接返回即可
        if (Regex.IsMatch(input, @"^\d{17}$"))
        {
            return input;
        }

        // 2. 尝试从各种群友乱发的链接中提取有效信息
        // 情况 A: 已经是带有 17 位 ID 的 profiles 链接 (例如 steamcommunity.com/profiles/76561198xxx)
        var profilesMatch = Regex.Match(input, @"profiles/(\d{17})");
        if (profilesMatch.Success)
        {
            return profilesMatch.Groups[1].Value;
        }

        // 情况 B: 带有自定义后缀的 URL 链接 
        string vanityName = input;
        var idMatch = Regex.Match(input, @"id/([^/?#]+)");
        if (idMatch.Success)
        {
            // 成功提取到后缀部分
            vanityName = idMatch.Groups[1].Value;
        }
        
        // 剥离可能残余的斜杠，保证传入 API 的是最纯净的字符串
        vanityName = vanityName.TrimEnd('/');

        // 3. 调用 Steam 官方解析接口，将自定义后缀转换为 17 位 ID
        try
        {
            string url = $"https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/?key={ApiKey}&vanityurl={Uri.EscapeDataString(vanityName)}";
            var json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var response = doc.RootElement.GetProperty("response");

            // success 为 1 代表转换成功
            if (response.GetProperty("success").GetInt32() == 1)
            {
                return response.GetProperty("steamid").GetString() ?? "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SteamID转换失败] {ex.Message}");
        }

        return ""; // 查无此人或网络波动
    }
}