using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace MilkyQQBot;

public static class DatabaseManager
{
    private const string DbPath = "Data Source=bot_messages.db";
    
    // 将正则表达式预编译为静态只读对象，大幅降低循环调用时的 CPU 消耗
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);

    public static void Initialize()
    {
        Console.WriteLine("[数据库] 正在初始化 SQLite 连接...");

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        var command = connection.CreateCommand();
        // 1. 群消息表
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS GroupMessages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MessageSeq INTEGER,
                GroupId INTEGER,
                SenderId INTEGER,
                SenderNickname TEXT,
                PlainText TEXT,
                RawSegmentsJson TEXT,
                ReceiveTime DATETIME
            );
            CREATE INDEX IF NOT EXISTS idx_group_time ON GroupMessages(GroupId, ReceiveTime);
        ";
        command.ExecuteNonQuery();
        
        // 2. 创建 Steam 绑定表
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS SteamBindings (
                QQId INTEGER PRIMARY KEY,
                SteamId TEXT NOT NULL
            );
        ";
        command.ExecuteNonQuery();

        // 3. 创建 深网查询权限表 (新增加的表)
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS QueryPermissions (
                UserId INTEGER PRIMARY KEY,
                RemainingCount INTEGER DEFAULT 0
            );
        ";
        command.ExecuteNonQuery();

        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();

        Console.WriteLine("[数据库] 群消息表、Steam绑定表、权限表就绪，已开启 WAL 高性能高并发模式。");

        StartAutoCleanupTask();
    }
    
    // ==========================================
    // 群消息管理
    // ==========================================
    public static void SaveGroupMessage(long messageSeq, long groupId, long senderId, string nickname, string plainText, object segments)
    {
        try
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO GroupMessages 
                (MessageSeq, GroupId, SenderId, SenderNickname, PlainText, RawSegmentsJson, ReceiveTime) 
                VALUES 
                (@Seq, @GroupId, @SenderId, @Nickname, @PlainText, @SegmentsJson, @Time)";

            command.Parameters.AddWithValue("@Seq", messageSeq);
            command.Parameters.AddWithValue("@GroupId", groupId);
            command.Parameters.AddWithValue("@SenderId", senderId);
            command.Parameters.AddWithValue("@Nickname", nickname);
            command.Parameters.AddWithValue("@PlainText", plainText);
            
            // 使用原生 System.Text.Json 进行序列化
            string segmentsJson = JsonSerializer.Serialize(segments);
            command.Parameters.AddWithValue("@SegmentsJson", segmentsJson);
            command.Parameters.AddWithValue("@Time", DateTime.Now);

            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[数据库写入失败] {ex.Message}");
        }
    }
    
    private static void StartAutoCleanupTask()
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(12));
            CleanUpOldMessages();

            while (await timer.WaitForNextTickAsync())
            {
                CleanUpOldMessages();
            }
        });
    }

    private static void CleanUpOldMessages()
    {
        try
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM GroupMessages WHERE ReceiveTime < @LimitTime";
            command.Parameters.AddWithValue("@LimitTime", DateTime.Now.AddDays(-365));
            
            int deletedRows = command.ExecuteNonQuery();
            if (deletedRows > 0)
            {
                Console.WriteLine($"[数据库自动清理] 删除了 {deletedRows} 条超过一年的过期群消息。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[数据库清理报错] {ex.Message}");
        }
    }
    
    public static List<UserActivityStat> GetGroupActivity(long groupId, DateTime startTime)
    {
        var stats = new Dictionary<long, UserActivityStat>();

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT SenderId, SenderNickname, PlainText, RawSegmentsJson 
            FROM GroupMessages 
            WHERE GroupId = @GroupId AND ReceiveTime >= @StartTime";
        
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@StartTime", startTime);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            long senderId = reader.GetInt64(0);
            string nickname = reader.GetString(1);
            string plainText = reader.GetString(2);
            string rawJson = reader.GetString(3);

            if (!stats.ContainsKey(senderId))
            {
                stats[senderId] = new UserActivityStat { SenderId = senderId, Nickname = nickname };
            }

            var stat = stats[senderId];
            stat.MessageCount++;
            
            int currentWordCount = plainText.Replace("\r", "").Replace("\n", "").Length;
            
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var rawText = element.GetRawText().ToLower();
                    if (rawText.Contains("image")) stat.ImageCount++;
                    if (rawText.Contains("face")) 
                    {
                        stat.FaceCount++;
                        currentWordCount++; 
                    }
                }
            }
            catch { }

            stat.WordCount += currentWordCount;
        }

        return stats.Values
            .OrderByDescending(x => x.MessageCount)
            .Take(5)
            .ToList();
    }
    
    public static List<string> GetGroupMessagesText(long groupId, DateTime startTime)
    {
        var texts = new List<string>();

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT PlainText FROM GroupMessages WHERE GroupId = @GroupId AND ReceiveTime >= @StartTime";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@StartTime", startTime);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string text = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }
        }
        return texts;
    }
    
    public static (string Nickname, string PlainText, string RawSegmentsJson) GetMessageBySeq(long groupId, long messageSeq)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        
        var command = connection.CreateCommand();
        command.CommandText = "SELECT SenderNickname, PlainText, RawSegmentsJson FROM GroupMessages WHERE GroupId = @GroupId AND MessageSeq = @Seq LIMIT 1";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@Seq", messageSeq);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }
        
        return (null, null, null); 
    }

    public static List<string> GetRecentGroupMessagesFormatted(long groupId, int limit = 50)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT SenderId, SenderNickname, RawSegmentsJson 
            FROM GroupMessages 
            WHERE GroupId = @GroupId 
            ORDER BY ReceiveTime DESC 
            LIMIT @Limit";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@Limit", limit * 2);

        using var reader = command.ExecuteReader();
        var tempList = new List<string>();
        
        while (reader.Read())
        {
            if (tempList.Count >= limit) break;

            long senderId = reader.GetInt64(0);
            string nickname = reader.GetString(1);
            string rawJson = reader.GetString(2);
            
            string pureText = ExtractPureTextFromJson(rawJson);
            
            if (!string.IsNullOrWhiteSpace(pureText))
            {
                // 现在机器人的消息也会完整保留并格式化进入 tempList
                tempList.Add($"[{senderId}][{nickname}]:{pureText}");
            }
        }
        
        tempList.Reverse();
        return tempList;
    }
    
    public static List<string> GetRecentBotReplies(long groupId, long botId, int limit = 15)
    {
        var replies = new List<string>();

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT PlainText
        FROM GroupMessages
        WHERE GroupId = @GroupId
          AND SenderId = @BotId
          AND PlainText IS NOT NULL
          AND TRIM(PlainText) != ''
        ORDER BY ReceiveTime DESC
        LIMIT @Limit";

        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@BotId", botId);
        command.Parameters.AddWithValue("@Limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string text = reader.GetString(0);

            if (!string.IsNullOrWhiteSpace(text))
            {
                replies.Add(text.Trim());
            }
        }

        return replies;
    }

    private static string ExtractPureTextFromJson(string rawJson)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            using var doc = JsonDocument.Parse(rawJson);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                string type = element.TryGetProperty("type", out var t) ? t.GetString() : "";
                
                if (type == "text")
                {
                    if (element.TryGetProperty("data", out var data) && data.TryGetProperty("text", out var textProp))
                    {
                        sb.Append(textProp.GetString());
                    }
                }
            }
            string text = sb.ToString();
            
            // 使用预编译的正则对象执行替换
            text = UrlRegex.Replace(text, "").Trim();
            return text;
        }
        catch
        {
            return "";
        }
    }
    
    // ==========================================
    // Steam 账号绑定与查询
    // ==========================================
    public static void BindSteam(long qqId, string steamId)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        var command = connection.CreateCommand();
        // 存在则更新，不存在则插入
        command.CommandText = @"
            INSERT INTO SteamBindings (QQId, SteamId) 
            VALUES (@QQId, @SteamId)
            ON CONFLICT(QQId) DO UPDATE SET SteamId = @SteamId;";
        command.Parameters.AddWithValue("@QQId", qqId);
        command.Parameters.AddWithValue("@SteamId", steamId);
        command.ExecuteNonQuery();
    }

    public static string GetSteamId(long qqId)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT SteamId FROM SteamBindings WHERE QQId = @QQId LIMIT 1";
        command.Parameters.AddWithValue("@QQId", qqId);
        var result = command.ExecuteScalar();
        return result?.ToString();
    }

    // ==========================================
    // 深网查询权限管理 (新增模块)
    // ==========================================
    
    // 1. 赋予/更新查询权限
    public static void GrantQueryPermission(long userId, int count)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var command = connection.CreateCommand();
        
        // 巧妙使用 ON CONFLICT DO UPDATE 来实现累加次数
        command.CommandText = @"
            INSERT INTO QueryPermissions (UserId, RemainingCount) 
            VALUES (@UserId, @Count)
            ON CONFLICT(UserId) DO UPDATE SET RemainingCount = RemainingCount + @Count;";
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Count", count);
        command.ExecuteNonQuery();
    }

    // 2. 获取剩余查询次数
    public static int GetRemainingQueryCount(long userId)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT RemainingCount FROM QueryPermissions WHERE UserId = @UserId LIMIT 1";
        command.Parameters.AddWithValue("@UserId", userId);
        
        var result = command.ExecuteScalar();
        // 防止数据为空或 DBNull 引起崩溃
        return (result != null && result != DBNull.Value) ? Convert.ToInt32(result) : 0;
    }

    // 3. 扣除一次查询机会
    public static void ConsumeQueryPermission(long userId)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE QueryPermissions SET RemainingCount = RemainingCount - 1 WHERE UserId = @UserId AND RemainingCount > 0";
        command.Parameters.AddWithValue("@UserId", userId);
        command.ExecuteNonQuery();
    }
    
    /// <summary>
    /// 获取“适合喂给 AI”的最近群消息。
    /// 与 GetRecentGroupMessagesFormatted 的区别：
    /// 这里会把图片、表情、@、回复等 segment 也转成可读上下文，而不是只保留纯文本。
    /// </summary>
    public static List<string> GetRecentGroupMessagesForAi(long groupId, int limit = 12)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
        SELECT MessageSeq, SenderId, SenderNickname, RawSegmentsJson
        FROM GroupMessages
        WHERE GroupId = @GroupId
        ORDER BY ReceiveTime DESC
        LIMIT @Limit";

        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@Limit", limit);

        using var reader = command.ExecuteReader();

        var tempList = new List<string>();

        while (reader.Read())
        {
            long messageSeq = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
            long senderId = reader.GetInt64(1);
            string nickname = reader.IsDBNull(2) ? "未知用户" : reader.GetString(2);
            string rawJson = reader.IsDBNull(3) ? "[]" : reader.GetString(3);

            string formattedContent = FormatSegmentsForAi(groupId, messageSeq, rawJson);

            if (!string.IsNullOrWhiteSpace(formattedContent))
            {
                tempList.Add($"[{senderId}][{nickname}]:{formattedContent}");
            }
        }

        // 数据库里是倒序取的，喂给 AI 前翻转回正序
        tempList.Reverse();
        return tempList;
    }

    /// <summary>
    /// 将一条消息的 RawSegmentsJson 转成适合给 AI 看的可读文本。
    /// 例如：
    /// - text -> 原样文本
    /// - image -> [图片]
    /// - face -> [表情]
    /// - mention -> [@123456]
    /// - reply -> [回复: xxx]
    /// </summary>
    private static string FormatSegmentsForAi(long groupId, long currentMessageSeq, string rawJson)
    {
        try
        {
            var sb = new System.Text.StringBuilder();

            using var doc = JsonDocument.Parse(rawJson);

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                string type = element.TryGetProperty("type", out var t)
                    ? t.GetString() ?? ""
                    : "";

                if (!element.TryGetProperty("data", out var data))
                {
                    data = default;
                }

                switch (type)
                {
                    case "text":
                    {
                        if (data.ValueKind != JsonValueKind.Undefined &&
                            data.TryGetProperty("text", out var textProp))
                        {
                            sb.Append(textProp.GetString());
                        }
                        break;
                    }

                    case "image":
                    {
                        // 第一版不做图片识别，先只告诉 AI 这里有图
                        AppendWithSpace(sb, "[图片]");
                        break;
                    }

                    case "face":
                    {
                        AppendWithSpace(sb, "[表情]");
                        break;
                    }

                    case "mention":
                    {
                        long userId = 0;
                        if (data.ValueKind != JsonValueKind.Undefined &&
                            data.TryGetProperty("user_id", out var userIdProp) &&
                            userIdProp.TryGetInt64(out var parsedUserId))
                        {
                            userId = parsedUserId;
                        }

                        AppendWithSpace(sb, userId > 0 ? $"[@{userId}]" : "[@某人]");
                        break;
                    }

                    case "mention_all":
                    {
                        AppendWithSpace(sb, "[@全体成员]");
                        break;
                    }

                    case "reply":
                    {
                        long replySeq = 0;
                        if (data.ValueKind != JsonValueKind.Undefined &&
                            data.TryGetProperty("message_seq", out var seqProp) &&
                            seqProp.TryGetInt64(out var parsedSeq))
                        {
                            replySeq = parsedSeq;
                        }

                        string replyText = BuildReplyHint(groupId, replySeq);
                        AppendWithSpace(sb, replyText);
                        break;
                    }
                }
            }

            string result = sb.ToString();

            // 去掉 URL，避免上下文里塞太多链接
            result = UrlRegex.Replace(result, "").Trim();

            // 压缩多余空白
            result = Regex.Replace(result, @"\s+", " ").Trim();

            return result;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 将 reply segment 转成简短提示。
    /// 如果能查到被回复的消息，就带一点摘要；查不到则保留为通用占位。
    /// </summary>
    private static string BuildReplyHint(long groupId, long replySeq)
    {
        if (replySeq <= 0)
            return "[回复某条消息]";

        var replied = GetMessageBySeq(groupId, replySeq);

        if (string.IsNullOrWhiteSpace(replied.PlainText))
            return "[回复某条消息]";

        string snippet = replied.PlainText.Trim();

        // 截断一下，避免回复提示太长污染上下文
        if (snippet.Length > 18)
        {
            snippet = snippet[..18] + "…";
        }

        if (!string.IsNullOrWhiteSpace(replied.Nickname))
        {
            return $"[回复 {replied.Nickname}: {snippet}]";
        }

        return $"[回复: {snippet}]";
    }

    /// <summary>
    /// 往 StringBuilder 里追加一个带空格分隔的片段，避免标记黏连。
    /// </summary>
    private static void AppendWithSpace(System.Text.StringBuilder sb, string value)
    {
        if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1]))
        {
            sb.Append(' ');
        }

        sb.Append(value);
    }
    
    /// <summary>
    /// 常见 QQ 系统表情 ID 映射成更容易理解的语义提示。
    /// 这里优先按 QQ 机器人官方文档中的 EmojiType=1（系统表情）补齐。
    /// 未知 ID 仍然回退到通用占位，避免线上新增表情导致异常。
    /// </summary>
    private static string MapFaceIdToHint(int faceId)
    {
        if (CommonFaceHints.TryGetValue(faceId, out var hint))
            return hint;

        return "[发了表情]";
    }
    
    
    /// <summary>
    /// QQ 系统表情（EmojiType=1）常见 ID -> 语义占位。
    /// 这些映射主要用于给 AI 看上下文，不追求百分之百“UI 文案一致”，
    /// 重点是让模型理解大概情绪和动作。
    /// </summary>
    private static readonly Dictionary<int, string> CommonFaceHints = new()
    {
        [4]   = "[发了得意表情]",
        [5]   = "[发了流泪表情]",
        [8]   = "[发了睡觉表情]",
        [9]   = "[发了大哭表情]",
        [10]  = "[发了尴尬表情]",
        [12]  = "[发了调皮表情]",
        [14]  = "[发了微笑表情]",
        [16]  = "[发了酷表情]",
        [21]  = "[发了可爱表情]",
        [23]  = "[发了傲慢表情]",
        [24]  = "[发了饥饿表情]",
        [25]  = "[发了困表情]",
        [26]  = "[发了惊恐表情]",
        [27]  = "[发了流汗表情]",
        [28]  = "[发了憨笑表情]",
        [29]  = "[发了悠闲表情]",
        [30]  = "[发了奋斗表情]",
        [32]  = "[发了疑问表情]",
        [33]  = "[发了嘘表情]",
        [34]  = "[发了晕表情]",
        [38]  = "[发了敲打表情]",
        [39]  = "[发了再见表情]",
        [41]  = "[发了发抖表情]",
        [42]  = "[发了爱情表情]",
        [43]  = "[发了跳跳表情]",
        [49]  = "[发了拥抱表情]",
        [53]  = "[发了蛋糕表情]",
        [60]  = "[发了咖啡表情]",
        [63]  = "[发了玫瑰表情]",
        [66]  = "[发了爱心表情]",
        [74]  = "[发了太阳表情]",
        [75]  = "[发了月亮表情]",
        [76]  = "[发了赞表情]",
        [78]  = "[发了握手表情]",
        [79]  = "[发了胜利表情]",
        [85]  = "[发了飞吻表情]",
        [89]  = "[发了西瓜表情]",
        [96]  = "[发了冷汗表情]",
        [97]  = "[发了擦汗表情]",
        [98]  = "[发了抠鼻表情]",
        [99]  = "[发了鼓掌表情]",
        [100] = "[发了糗大了表情]",
        [101] = "[发了坏笑表情]",
        [102] = "[发了左哼哼表情]",
        [103] = "[发了右哼哼表情]",
        [104] = "[发了哈欠表情]",
        [106] = "[发了委屈表情]",
        [109] = "[发了左亲亲表情]",
        [111] = "[发了可怜表情]",
        [116] = "[发了示爱表情]",
        [118] = "[发了抱拳表情]",
        [120] = "[发了拳头表情]",
        [122] = "[发了爱你表情]",
        [123] = "[发了NO表情]",
        [124] = "[发了OK表情]",
        [125] = "[发了转圈表情]",
        [129] = "[发了挥手表情]",
        [144] = "[发了喝彩表情]",
        [147] = "[发了棒棒糖表情]",
        [171] = "[发了茶表情]",
        [173] = "[发了泪奔表情]",
        [174] = "[发了无奈表情]",
        [175] = "[发了卖萌表情]",
        [176] = "[发了小纠结表情]",
        [179] = "[发了doge表情]",
        [180] = "[发了惊喜表情]",
        [181] = "[发了骚扰表情]",
        [182] = "[发了笑哭表情]",
        [183] = "[发了我最美表情]",
        [201] = "[发了点赞表情]",
        [203] = "[发了托脸表情]",
        [212] = "[发了托腮表情]",
        [214] = "[发了啵啵表情]",
        [219] = "[发了蹭一蹭表情]",
        [222] = "[发了抱抱表情]",
        [227] = "[发了拍手表情]",
        [232] = "[发了佛系表情]",
        [240] = "[发了喷脸表情]",
        [243] = "[发了甩头表情]",
        [246] = "[发了加油抱抱表情]",
        [262] = "[发了脑阔疼表情]",
        [264] = "[发了捂脸表情]",
        [265] = "[发了辣眼睛表情]",
        [266] = "[发了哦哟表情]",
        [267] = "[发了头秃表情]",
        [268] = "[发了问号脸表情]",
        [269] = "[发了暗中观察表情]",
        [270] = "[发了emm表情]",
        [271] = "[发了吃瓜表情]",
        [272] = "[发了呵呵哒表情]",
        [273] = "[发了我酸了表情]",
        [277] = "[发了汪汪表情]",
        [278] = "[发了汗表情]",
        [281] = "[发了无眼笑表情]",
        [282] = "[发了敬礼表情]",
        [284] = "[发了面无表情表情]",
        [285] = "[发了摸鱼表情]",
        [287] = "[发了哦表情]",
        [289] = "[发了睁眼表情]",
        [290] = "[发了敲开心表情]",
        [293] = "[发了摸锦鲤表情]",
        [294] = "[发了期待表情]",
        [297] = "[发了拜谢表情]",
        [298] = "[发了元宝表情]",
        [299] = "[发了牛啊表情]",
        [305] = "[发了右亲亲表情]",
        [306] = "[发了牛气冲天表情]",
        [307] = "[发了喵喵表情]",
        [314] = "[发了仔细分析表情]",
        [315] = "[发了加油表情]",
        [318] = "[发了崇拜表情]",
        [319] = "[发了比心表情]",
        [320] = "[发了庆祝表情]",
        [322] = "[发了拒绝表情]",
        [324] = "[发了吃糖表情]",
        [326] = "[发了生气表情]"
    };
}