using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MilkyQQBot;

// 根据 API 返回结果构建的数据模型
public class PrivacyApiResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("message")]
    public string Message { get; set; }
    
    [JsonPropertyName("data")]
    public PrivacyData Data { get; set; }
}

public class PrivacyData
{
    public List<string> names { get; set; }
    public List<string> nicknames { get; set; }
    public List<string> phone_numbers { get; set; }
    public List<string> id_numbers { get; set; }
    public List<string> qq_numbers { get; set; }
    public List<string> wb_numbers { get; set; }
    public List<string> passwords { get; set; }
    public List<string> emails { get; set; }
    public List<string> addresses { get; set; }
}

public static class PrivacyQueryService
{
    private static readonly HttpClient _httpClient = new();

    // 【新增这部分】给 HttpClient 穿上伪装衣，骗过对方服务器的防火墙
    static PrivacyQueryService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://privacy.aiuys.com/");
    }

    public static async Task<string> SearchAsync(string target)
    {
        string url = $"https://privacy.aiuys.com/api/query?value={Uri.EscapeDataString(target)}";

        try
        {
            // 注意：因为对方可能会返回 403 抛出异常，为了更精准地捕获，我们这里稍微改写一下抓取逻辑
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                // 如果伪装后还是被拦截，打印具体的 HTTP 状态码方便排查
                Console.WriteLine($"[深网查询警告] 接口返回状态码: {response.StatusCode}");
                return $"❌ 查询失败，接口拒绝访问 (HTTP {(int)response.StatusCode})。可能是触发了对方的防爬虫机制。";
            }

            var json = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<PrivacyApiResponse>(json);
            
            // ... 后面的解析逻辑保持你原来的不变 ...
            if (apiResponse == null || apiResponse.Code != 0 || apiResponse.Data == null)
            {
                return "❌ 查询失败，接口维护或暂无数据响应。";
            }
            
            var data = apiResponse.Data;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🔎 奈奈川深网检索报告：[{target}]");
            sb.AppendLine("------------------------");
            
            bool foundAny = false;
            
            // 提取数据并格式化输出
            if (data.names?.Count > 0) { sb.AppendLine($"👤 姓名: {string.Join(", ", data.names)}"); foundAny = true; }
            if (data.nicknames?.Count > 0) { sb.AppendLine($"🏷️ 昵称: {string.Join(", ", data.nicknames)}"); foundAny = true; }
            if (data.phone_numbers?.Count > 0) { sb.AppendLine($"📱 手机: {string.Join("\n      ", data.phone_numbers)}"); foundAny = true; }
            if (data.id_numbers?.Count > 0) { sb.AppendLine($"🪪 证件: {string.Join(", ", data.id_numbers)}"); foundAny = true; }
            if (data.qq_numbers?.Count > 0) { sb.AppendLine($"🐧 QQ: {string.Join(", ", data.qq_numbers)}"); foundAny = true; }
            if (data.emails?.Count > 0) { sb.AppendLine($"📧 邮箱: {string.Join(", ", data.emails)}"); foundAny = true; }
            if (data.addresses?.Count > 0) { sb.AppendLine($"🏠 地址: {string.Join("\n      ", data.addresses)}"); foundAny = true; }
            if (data.passwords?.Count > 0) { sb.AppendLine($"🔑 泄露密码: {string.Join(", ", data.passwords)}"); foundAny = true; }
            
            // ==========================================
            // 【修改部分】优化微博数据的解析与主页链接拼接
            // ==========================================
            if (data.wb_numbers?.Count > 0) 
            {
                var wbClean = new List<string>();
                foreach (var wb in data.wb_numbers)
                {
                    // 使用 %:|:% 作为分隔符切开字符串
                    var parts = wb.Split(new[] { "%:|:%" }, StringSplitOptions.None);
                    
                    if (parts.Length > 0)
                    {
                        string uid = parts[0]; // 前半部分是 UID
                        string desc = parts.Length > 1 ? parts[1] : "微博用户"; // 后半部分是描述
                        
                        // 拼接出美观的排版和直达链接
                        wbClean.Add($"{uid} [{desc}]\n        🔗 主页: https://weibo.com/u/{uid}");
                    }
                    else
                    {
                        wbClean.Add(wb); // 兜底防报错
                    }
                }
                sb.AppendLine($"👁️ 微博: {string.Join("\n      ", wbClean)}");
                foundAny = true;
            }
            
            if (!foundAny)
            {
                return $"📂 目标 [{target}] 很安全，深网数据库中未检索到相关泄露记录。";
            }
            
            sb.AppendLine("------------------------");
            sb.Append("⚠️ 警告：结果来源于公开泄露库，仅供自查，请勿用于非法用途！");
            
            return sb.ToString();

        }
        catch (Exception ex)
        {
            Console.WriteLine($"[泄露查询错误] {ex.Message}");
            return "❌ API 连接异常，可能是服务器强行掐断了连接。";
        }
    }
}