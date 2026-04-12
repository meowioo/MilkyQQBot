using System;
using System.Collections.Generic;
using System.Linq;

namespace MilkyQQBot.Game;

public static class GameBattleEngine
{
    private static readonly Random Rng = new();

    /// <summary>
    /// 触发一次自动战斗。
    /// 设计目标：
    /// 1. 玩家先手
    /// 2. 使用固定伤害公式，便于平衡和调试
    /// 3. 胜利给金币，失败回起点并扣 10% 金币
    /// </summary>
    public static BattleResult TriggerBattle(GamePlayer player)
    {
        var monster = RollMonster();

        int monsterHp = monster.HP;
        int playerHpBefore = player.HP;
        int goldBefore = player.Gold;

        var result = new BattleResult
        {
            Monster = monster,
            PlayerHpBefore = playerHpBefore,
            GoldBefore = goldBefore
        };

        result.Logs.Add($"⚔️ 你踩进了战斗格！");
        result.Logs.Add($"{monster.Emoji} 一只 {monster.Name} 跳了出来！");

        // 如果玩家当前已经 0 血，先兜底恢复到 1，避免出现“0血还在打”的异常情况
        if (player.HP <= 0)
        {
            player.HP = 1;
        }

        while (player.HP > 0 && monsterHp > 0)
        {
            // ===== 玩家先手 =====
            int playerDamage = Math.Max(1, player.ATK - monster.DEF);
            monsterHp -= playerDamage;
            result.Logs.Add($"🗡️ 你对 {monster.Name} 造成了 {playerDamage} 点伤害。");

            if (monsterHp <= 0)
            {
                break;
            }

            // ===== 怪物反击 =====
            int monsterDamage = Math.Max(1, monster.ATK - player.DEF);
            player.HP -= monsterDamage;
            result.Logs.Add($"{monster.Emoji} {monster.Name} 对你造成了 {monsterDamage} 点伤害。");
        }

        if (player.HP > 0)
        {
            // 胜利
            int reward = Rng.Next(monster.GoldMin, monster.GoldMax + 1);
            player.Gold += reward;
            player.KillCount += 1;

            result.PlayerWon = true;
            result.RewardGold = reward;
            result.PlayerHpAfter = player.HP;
            result.GoldAfter = player.Gold;

            result.Logs.Add($"🎉 你击败了 {monster.Name}！");
            result.Logs.Add($"🪙 获得 {reward} 枚金币。");
            result.Logs.Add($"❤️ 当前生命：{player.HP}/{player.MaxHP}");
        }
        else
        {
            // 失败：进入重伤状态
            player.DeathCount += 1;

            int loseGold = (int)Math.Floor(player.Gold * GameBattleBalance.DefeatGoldLoseRate);
            loseGold = Math.Min(loseGold, player.Gold);

            player.Gold -= loseGold;
            player.HP = Math.Min(GameBattleBalance.DefeatRecoverHp, player.MaxHP);

            // 回起点
            player.Step = 0;
            player.Direction = 1;

            result.PlayerWon = false;
            result.LostGold = loseGold;
            result.PlayerHpAfter = player.HP;
            result.GoldAfter = player.Gold;

            result.Logs.Add($"💥 你被 {monster.Name} 击败了！");
            result.Logs.Add($"🩸 进入重伤状态，已回到起点。");
            result.Logs.Add($"💸 丢失 {loseGold} 枚金币。");
            result.Logs.Add($"❤️ 生命恢复为 {player.HP}/{player.MaxHP}");
        }

        return result;
    }

    /// <summary>
    /// 按权重随机刷怪
    /// </summary>
    private static MonsterDef RollMonster()
    {
        int totalWeight = GameMonsterLibrary.All.Sum(m => m.Weight);
        int roll = Rng.Next(1, totalWeight + 1);

        int current = 0;
        foreach (var monster in GameMonsterLibrary.All)
        {
            current += monster.Weight;
            if (roll <= current)
            {
                return monster;
            }
        }

        return GameMonsterLibrary.All[0];
    }
}