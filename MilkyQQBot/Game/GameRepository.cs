using Microsoft.Data.Sqlite;

namespace MilkyQQBot.Game;

public static class GameRepository
{
    private const string DbPath = "Data Source=bot_messages.db";

    public static void Initialize()
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS GamePlayers (
    GroupId    INTEGER NOT NULL,
    UserId     INTEGER NOT NULL,
    Nickname   TEXT    NOT NULL,
    Step       INTEGER NOT NULL DEFAULT 0,
    Direction  INTEGER NOT NULL DEFAULT 1,
    HP         INTEGER NOT NULL DEFAULT 100,
    MaxHP      INTEGER NOT NULL DEFAULT 100,
    ATK        INTEGER NOT NULL DEFAULT 10,
    DEF        INTEGER NOT NULL DEFAULT 5,
    Gold       INTEGER NOT NULL DEFAULT 10,
    KillCount  INTEGER NOT NULL DEFAULT 0,
    DeathCount INTEGER NOT NULL DEFAULT 0,
    JoinTime   TEXT    NOT NULL,
    PRIMARY KEY (GroupId, UserId)
);
CREATE INDEX IF NOT EXISTS idx_game_players_group ON GamePlayers(GroupId);
";
        command.ExecuteNonQuery();

        // 兼容旧表：如果用户本地数据库是在老版本创建的，这里自动补列
        EnsureColumnExists(connection, "GamePlayers", "KillCount", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "GamePlayers", "DeathCount", "INTEGER NOT NULL DEFAULT 0");

        Console.WriteLine("[数据库] 游戏玩家表已就绪。");
    }

    public static bool CreatePlayer(long groupId, long userId, string nickname)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT OR IGNORE INTO GamePlayers
(GroupId, UserId, Nickname, Step, Direction, HP, MaxHP, ATK, DEF, Gold, KillCount, DeathCount, JoinTime)
VALUES
(@GroupId, @UserId, @Nickname, 0, 1, 100, 100, 10, 5, @Gold, 0, 0, @JoinTime);
";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Nickname", nickname);
        command.Parameters.AddWithValue("@Gold", GameBalance.InitialGold);
        command.Parameters.AddWithValue("@JoinTime", DateTime.Now.ToString("O"));

        return command.ExecuteNonQuery() > 0;
    }

    public static void UpdateNickname(long groupId, long userId, string nickname)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE GamePlayers
SET Nickname = @Nickname
WHERE GroupId = @GroupId AND UserId = @UserId;
";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Nickname", nickname);
        command.ExecuteNonQuery();
    }

    public static GamePlayer? GetPlayer(long groupId, long userId)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT GroupId, UserId, Nickname, Step, Direction, HP, MaxHP, ATK, DEF, Gold, KillCount, DeathCount, JoinTime
FROM GamePlayers
WHERE GroupId = @GroupId AND UserId = @UserId
LIMIT 1;
";
        command.Parameters.AddWithValue("@GroupId", groupId);
        command.Parameters.AddWithValue("@UserId", userId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapPlayer(reader);
    }

    public static List<GamePlayer> GetPlayers(long groupId)
    {
        var result = new List<GamePlayer>();

        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT GroupId, UserId, Nickname, Step, Direction, HP, MaxHP, ATK, DEF, Gold, KillCount, DeathCount, JoinTime
FROM GamePlayers
WHERE GroupId = @GroupId
ORDER BY JoinTime ASC;
";
        command.Parameters.AddWithValue("@GroupId", groupId);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(MapPlayer(reader));
        }

        return result;
    }

    public static void UpdatePlayer(GamePlayer player)
    {
        using var connection = new SqliteConnection(DbPath);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE GamePlayers
SET Nickname = @Nickname,
    Step = @Step,
    Direction = @Direction,
    HP = @HP,
    MaxHP = @MaxHP,
    ATK = @ATK,
    DEF = @DEF,
    Gold = @Gold,
    KillCount = @KillCount,
    DeathCount = @DeathCount
WHERE GroupId = @GroupId AND UserId = @UserId;
";
        command.Parameters.AddWithValue("@GroupId", player.GroupId);
        command.Parameters.AddWithValue("@UserId", player.UserId);
        command.Parameters.AddWithValue("@Nickname", player.Nickname);
        command.Parameters.AddWithValue("@Step", player.Step);
        command.Parameters.AddWithValue("@Direction", player.Direction);
        command.Parameters.AddWithValue("@HP", player.HP);
        command.Parameters.AddWithValue("@MaxHP", player.MaxHP);
        command.Parameters.AddWithValue("@ATK", player.ATK);
        command.Parameters.AddWithValue("@DEF", player.DEF);
        command.Parameters.AddWithValue("@Gold", player.Gold);
        command.Parameters.AddWithValue("@KillCount", player.KillCount);
        command.Parameters.AddWithValue("@DeathCount", player.DeathCount);
        command.ExecuteNonQuery();
    }

    private static GamePlayer MapPlayer(SqliteDataReader reader)
    {
        string joinTimeText = reader.GetString(12);
        DateTime joinTime = DateTime.TryParse(joinTimeText, out var parsed)
            ? parsed
            : DateTime.Now;

        return new GamePlayer
        {
            GroupId = reader.GetInt64(0),
            UserId = reader.GetInt64(1),
            Nickname = reader.GetString(2),
            Step = reader.GetInt32(3),
            Direction = reader.GetInt32(4),
            HP = reader.GetInt32(5),
            MaxHP = reader.GetInt32(6),
            ATK = reader.GetInt32(7),
            DEF = reader.GetInt32(8),
            Gold = reader.GetInt32(9),
            KillCount = reader.GetInt32(10),
            DeathCount = reader.GetInt32(11),
            JoinTime = joinTime
        };
    }

    /// <summary>
    /// 为旧数据库自动补列
    /// </summary>
    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName});";

        bool exists = false;
        using (var reader = checkCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                string existingColumnName = reader.GetString(1);
                if (string.Equals(existingColumnName, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();

        Console.WriteLine($"[数据库] 已为 {tableName} 补充列：{columnName}");
    }
}