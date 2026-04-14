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
}