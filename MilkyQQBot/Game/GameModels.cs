namespace MilkyQQBot.Game;

public sealed class GamePlayer
{
    public long GroupId { get; set; }
    public long UserId { get; set; }
    public string Nickname { get; set; } = string.Empty;

    public int Step { get; set; } = 0;
    public int Direction { get; set; } = 1; // 1 = 正向, -1 = 反向

    public int HP { get; set; } = 100;
    public int MaxHP { get; set; } = 100;
    public int ATK { get; set; } = 10;
    public int DEF { get; set; } = 5;
    public int Gold { get; set; } = 0;

    public DateTime JoinTime { get; set; } = DateTime.Now;
}

public readonly record struct GameNode(int X, int Y);
