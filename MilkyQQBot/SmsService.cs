using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MilkyQQBot;

public static class SmsService
{
    private static readonly HttpClient _httpClient = new();
    
    private static string ApiBaseUrl => AppConfig.Current.Sms.ApiUrl;
    private static string FcToken => AppConfig.Current.Sms.Token;

    static SmsService()
    {
        // 所有接口均需在 Header 中提供 fcToken 进行鉴权
        _httpClient.DefaultRequestHeaders.Add("fcToken", FcToken);
    }

    // 1. 获取项目列表
    public static async Task<string> SearchProjectAsync(string keyword)
    {
        string url = $"{ApiBaseUrl}/api/user/projects?project_name={Uri.EscapeDataString(keyword)}";
        
        try
        {
            var response = await _httpClient.GetAsync(url);
            var responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);
            
            if (doc.RootElement.GetProperty("code").GetInt32() == 1)
            {
                var dataArray = doc.RootElement.GetProperty("data");
                if (dataArray.GetArrayLength() == 0) return "❌ 未找到相关项目，请更换关键词。";

                StringBuilder sb = new StringBuilder("🔍 检索到的项目如下：\n");
                int count = 0;
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (count >= 5) break; // 最多展示5个，防止刷屏
                    string id = item.GetProperty("id").ToString();
                    string name = item.GetProperty("project_name").GetString() ?? "未知";
                    string money = item.GetProperty("money").GetString() ?? "0";
                    sb.AppendLine($"ID: {id} | 价格: {money}元 | 名称: {name}");
                    count++;
                }
                sb.AppendLine("\n💡 请使用指令：/取号 [项目ID] 来获取手机号。");
                return sb.ToString().TrimEnd();
            }
            return $"❌ 查询失败：{doc.RootElement.GetProperty("msg").GetString()}";
        }
        catch (Exception ex)
        {
            return $"❌ 接口请求异常：{ex.Message}";
        }
    }

    // 2. 获取手机号
    public static async Task<string> GetPhoneNumberAsync(string projectId)
    {
        // 【终极必杀技】：放弃 Body，直接把参数硬拼到 URL 上 (Query 传参)
        string url = $"{ApiBaseUrl}/api/user/getPhone?project_id={projectId}";
        
        // 随便塞一个空的 StringContent，仅仅是为了维持 POST 的请求动作
        var content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

        try
        {
            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            string trimmedResponse = responseString.TrimStart();
            if (!trimmedResponse.StartsWith("{") && !trimmedResponse.StartsWith("["))
            {
                Console.WriteLine($"\n[接码 API 返回了非 JSON 数据]:\n{responseString}\n");
                return $"❌ 接口请求失败：服务器返回了网页而不是数据。";
            }

            using var doc = JsonDocument.Parse(responseString);
            
            if (doc.RootElement.GetProperty("code").GetInt32() == 1)
            {
                var data = doc.RootElement.GetProperty("data");
                string phone = data.GetProperty("phone").GetString() ?? "";
                string sp = data.GetProperty("sp").GetString() ?? "未知";
                
                return $"✅ 取号成功！\n📱 手机号：{phone}\n🏢 运营商：{sp}\n\n💡 验证码发送后，请使用指令：/拿码 {projectId} {phone}";
            }
            return $"❌ 取号失败：{doc.RootElement.GetProperty("msg").GetString()}";
        }
        catch (Exception ex)
        {
            return $"❌ 接口请求异常：{ex.Message}";
        }
    }

    // 3. 获取验证码 (成功后扣费)
    public static async Task<string> GetVerifyCodeAsync(string projectId, string phone)
    {
        // 【终极必杀技】：同样把两个参数强行塞进 URL
        string url = $"{ApiBaseUrl}/api/user/getVerifyCode?project_id={projectId}&phone={phone}";
        
        var content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

        try
        {
            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();
            
            string trimmedResponse = responseString.TrimStart();
            if (!trimmedResponse.StartsWith("{") && !trimmedResponse.StartsWith("["))
            {
                return $"❌ 接口请求失败：服务器返回了非预期格式。";
            }

            using var doc = JsonDocument.Parse(responseString);
            
            int code = doc.RootElement.GetProperty("code").GetInt32();
            string msg = doc.RootElement.GetProperty("msg").GetString() ?? "";

            if (code == 1)
            {
                return $"✅ 验证码获取成功 (已扣费)！\n\n📩 短信内容：\n{msg}";
            }
            else
            {
                return $"⏳ 暂未收到验证码或获取失败：{msg}\n(请稍等几秒后再试一次)";
            }
        }
        catch (Exception ex)
        {
            return $"❌ 接口请求异常：{ex.Message}";
        }
    }
}