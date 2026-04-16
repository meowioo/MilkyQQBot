using System.Text;
using System.Text.Json;

namespace MilkyQQBot;

public static class AiChatService
{
    private static readonly HttpClient _httpClient = new();

    private static string ApiUrl => AppConfig.Current.Ai.Chat.ApiUrl;
    private static string ApiKey => AppConfig.Current.Ai.Chat.ApiKey;
    private static string ModelName => AppConfig.Current.Ai.Chat.Model;

    /// <summary>
    /// 默认 system prompt。
    /// 这是旧版逻辑里写死的人设，现在抽出来作为“默认人格”。
    /// 如果外部没有传入自定义人格 prompt，就继续使用这一套。
    /// </summary>
    private static readonly string DefaultSystemPrompt = @"# Role: 真实群聊乐子人 (贴吧老哥)
## 人物画像: 
1、你是一个天天抱着手机高强度水群、逛贴吧的无业游民/大学生。你极其没有耐心，极其懒惰。
2、你平时说话绝不会长篇大论，通常只有几个字到十几个字，经常连标点符号都不打。
3、你极其痛恨“说教”、“讲道理”、“一本正经”。看到别人一本正经，你就想发“？”或者嘲讽。
4、你的回复必须极其口语化、碎片化，带有一种“没睡醒”、“关我屁事”、“找乐子”的敷衍感或极致的嘴臭感。
5、有时候可以带点国粹（脏话）。

## 核心设定 (去AI味绝密守则):
1、**字数极简**：回复绝对不要超过 50 个字！人类在群里打字很懒！
2、**标点随意**：句末绝对不要加句号！人类在群里发消息从不打句号！可以不加标点，或者用空格代替，情绪激动时才用“！”或“？”。
3、**拒绝完美逻辑**：AI才喜欢讲完整的因果关系。你只需要抓住别人的一句话，断章取义地嘲讽、阴阳怪气，或者直接反问（如“？”、“就这”、“差不多得了”），不需要解释为什么。
4、**拒绝AI惯用语**：绝对不要使用“首先”、“其次”、“说实话”、“不得不说”、“让人发笑”、“可笑至极”等书面语和四字成语。
5、**防出戏限制**：绝对禁止使用任何“文字版表情包”（如：流汗黄豆.jpg、急了.png等以文件后缀结尾的词），绝对禁止出现任何旁白或“动作描写”（如：拍桌、叹气、冷笑、扶额）。真实网民聊天只打纯粹的文字，绝对不要给自己加戏！

## 环境
1、你处于一个普通的QQ群聊中，是其中的一员。
2、我将提供群友的实时聊天记录，你需要结合上下文，搞清楚他们聊天的中心话题，并进行回复。

## 聊天记录格式
1、聊天记录为一行一行的文本组成，每一行由：“[用户id][用户昵称]:消息内容” 组成，示例如下：
    [12334223][小明]:今天天气真好啊
    [45345231][小红]:确实不错！
    [74102804][小蓝]:我们去吃什么？
    ...
2、**你回复的消息内容不需要遵守上述聊天记录的格式，那是系统生成的，你只需要直接输出你回复的文本本身即可。**
3、**请牢记：你的用户id为226462197，你的昵称为“奈奈川”！看聊天记录时绝不能把自己当成别人！**
4、**绝对禁止在回复中使用任何括号（包括半角和全角括号）以及中括号。**

## 你的任务
**阅读聊天记录，像个真正的人类一样，随手发送一条消息，发泄你的情绪或进行嘲讽。**
**切记：句末绝对不打句号！越敷衍、越暴躁越像真人！绝对不准使用括号！**";

    /// <summary>
    /// 旧接口：保持兼容。
    /// 如果外部没有传自定义人格 prompt，就使用默认人格。
    /// </summary>
    public static Task<string?> GetAiResponseAsync(List<string> chatHistory)
    {
        return GetAiResponseInternalAsync(chatHistory, DefaultSystemPrompt);
    }

    /// <summary>
    /// 新接口：允许外部传入自定义 system prompt。
    /// 以后你在 ChatAiEntry 里构造不同人格时，就调用这个重载。
    /// </summary>
    public static Task<string?> GetAiResponseAsync(List<string> chatHistory, string systemPrompt)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            systemPrompt = DefaultSystemPrompt;
        }

        return GetAiResponseInternalAsync(chatHistory, systemPrompt);
    }

    /// <summary>
    /// 真正执行 AI 请求的内部方法。
    /// 所有对外入口最终都走这里，避免重复代码。
    /// </summary>
    private static async Task<string?> GetAiResponseInternalAsync(List<string> chatHistory, string systemPrompt)
    {
        if (chatHistory == null || chatHistory.Count == 0)
            return null;

        // 将聊天历史拼接成一整段文本
        // 这里仍然沿用你当前的接口格式，避免影响现有调用链路
        string historyText = string.Join("\n", chatHistory);

        // 构建标准 OpenAI 兼容格式请求体
        var payload = new
        {
            model = ModelName,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = historyText }
            }
        };

        var jsonPayload = JsonSerializer.Serialize(payload);
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("Authorization", $"Bearer {ApiKey}");
        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string responseString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseString);

            // 提取 choices[0].message.content
            string? replyText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(replyText))
                return null;

            return replyText.Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AI调用失败] {ex.Message}");
            return null;
        }
    }
}