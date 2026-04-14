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
}