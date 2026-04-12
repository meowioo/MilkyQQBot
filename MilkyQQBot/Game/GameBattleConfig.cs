using System;
using System.Collections.Generic;

namespace MilkyQQBot.Game;

/// <summary>
/// 战斗相关的全局平衡参数
/// </summary>
public static class GameBattleBalance
{
    // 战斗失败后，生命恢复到多少
    public const int DefeatRecoverHp = 50;

    // 战斗失败后，扣除当前金币的百分比
    public const double DefeatGoldLoseRate = 0.10;

    // 战斗格：10 个
    public static readonly HashSet<int> BattleSteps = new()
    {
        6, 15, 23, 30, 39, 47, 56, 65, 74, 83
    };
}

public sealed class MonsterDef
{
    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;

    public int HP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; }

    public int GoldMin { get; set; }
    public int GoldMax { get; set; }

    // 权重，值越大越容易刷出来
    public int Weight { get; set; }
}

public sealed class BattleResult
{
    public bool PlayerWon { get; set; }
    public MonsterDef Monster { get; set; } = new();
    public int RewardGold { get; set; }

    public int PlayerHpBefore { get; set; }
    public int PlayerHpAfter { get; set; }

    public int GoldBefore { get; set; }
    public int GoldAfter { get; set; }

    public int LostGold { get; set; }

    public List<string> Logs { get; set; } = new();
}

public static class GameMonsterLibrary
{
    public static readonly MonsterDef[] All = new[]
    {
        new MonsterDef
        {
            Name = "史莱姆",
            Emoji = "🟢",
            HP = 18,
            ATK = 6,
            DEF = 1,
            GoldMin = 2,
            GoldMax = 4,
            Weight = 40
        },
        new MonsterDef
        {
            Name = "哥布林",
            Emoji = "👺",
            HP = 28,
            ATK = 9,
            DEF = 2,
            GoldMin = 3,
            GoldMax = 6,
            Weight = 30
        },
        new MonsterDef
        {
            Name = "野猪",
            Emoji = "🐗",
            HP = 40,
            ATK = 11,
            DEF = 3,
            GoldMin = 4,
            GoldMax = 7,
            Weight = 22
        },
        new MonsterDef
        {
            Name = "宝箱怪",
            Emoji = " mimic",
            HP = 22,
            ATK = 8,
            DEF = 1,
            GoldMin = 6,
            GoldMax = 10,
            Weight = 8
        }
    };
}