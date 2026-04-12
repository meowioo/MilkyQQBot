namespace MilkyQQBot.Game;

public sealed class GamePlayer
{
    public long GroupId { get; set; }
    public long UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;

    public int Step { get; set; } = 0;

    /// <summary>
    /// 1 = 正向前进，-1 = 折返方向
    /// </summary>
    public int Direction { get; set; } = 1;

    public int HP { get; set; } = 100;
    public int MaxHP { get; set; } = 100;
    public int ATK { get; set; } = 10;
    public int DEF { get; set; } = 5;

    public int Gold { get; set; } = GameBalance.InitialGold;

    /// <summary>
    /// 击杀次数
    /// </summary>
    public int KillCount { get; set; }

    /// <summary>
    /// 阵亡次数
    /// </summary>
    public int DeathCount { get; set; }

    public bool IsWounded => HP > 0 && HP <= 50;

    public DateTime JoinTime { get; set; } = DateTime.Now;
}

public readonly record struct GameNode(int X, int Y);