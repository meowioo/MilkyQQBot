using System.Text.RegularExpressions;
using Milky.Net.Client;
using Milky.Net.Model;
using MilkyQQBot.Services;

namespace MilkyQQBot.Game;

public static class GameCommands
{
    private static readonly Regex MentionRegex = new(@"\[@(\d+)\]", RegexOptions.Compiled);
    private static readonly string MapPath = Path.Combine(AppContext.BaseDirectory, "Game/Assets", "QQMap.png");

    public static void Register(CommandHandler commandHandler, MilkyClient milky)
    {
        commandHandler.RegisterCommand("/helpg", async context =>
        {
            if (context.Scene != "group")
            {
                await context.ReplyToMessageAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            try
            {
                string base64Image = await GameImageGenerator.GenerateHelpMenuAsync();
                await context.ImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyToMessageAsync($"❌ 帮助菜单生成失败：{ex.Message}");
            }
        });
        
        commandHandler.RegisterCommand("/game", async context =>
        {
            if (context.Scene != "group")
            {
                await context.ReplyToMessageAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            if (context.SenderRole != "Owner" && context.SenderRole != "Admin")
            {
                await context.ReplyToMessageAsync("⚠️ 权限不足！该指令仅限群主或管理员使用。");
                return;
            }

            bool isEnabled = GroupConfigManager.ToggleGame(context.PeerId);
            if (isEnabled)
            {
                await context.ReplyToMessageAsync("✅ 已开启本群冒险游戏！现在可以使用 /join /go /look /info");
            }
            else
            {
                await context.ReplyToMessageAsync("❌ 已关闭本群冒险游戏。");
            }
        });

        commandHandler.RegisterCommand("/join", async context =>
        {
            if (!await EnsureGameEnabledAsync(context))
            {
                return;
            }

            string displayName = await GroupMemberHelper.GetDisplayNameAsync(
                milky,
                context.PeerId,
                context.SenderId,
                context.SenderId.ToString());

            bool created = GameRepository.CreatePlayer(context.PeerId, context.SenderId, displayName);
            GameRepository.UpdateNickname(context.PeerId, context.SenderId, displayName);

            if (created)
            {
                await context.ReplyToMessageAsync($"✅ {displayName} 加入成功，当前位置：第 0 步。");
            }
            else
            {
                var player = GameRepository.GetPlayer(context.PeerId, context.SenderId);
                int step = player?.Step ?? 0;
                await context.ReplyToMessageAsync($"ℹ️ 你已经加入过游戏了，当前在第 {step} 步。");
            }
        });

        commandHandler.RegisterCommand("/go", async context =>
{
    if (!await EnsureGameEnabledAsync(context))
    {
        return;
    }

    var players = GameRepository.GetPlayers(context.PeerId);
    var player = players.FirstOrDefault(p => p.UserId == context.SenderId);

    if (player is null)
    {
        await context.ReplyToMessageAsync("你还没加入游戏。");
        return;
    }

    string displayName = await GroupMemberHelper.GetDisplayNameAsync(
        milky,
        context.PeerId,
        context.SenderId,
        player.Nickname);

    player.Nickname = displayName;

    // 注意：Next(0, 6) 才会得到 0~5
    int roll = Random.Shared.Next(0, 6);

    var messages = new List<string>();

    if (roll == 0)
    {
        messages.Add("🎲 你走了 0 步，原地摸鱼了一会儿。");
        messages.Add($"📍 当前位于第 {player.Step} 步。");
        messages.Add($"🪙 当前金币：{player.Gold}");
        messages.Add($"❤️ 当前生命：{player.HP}/{player.MaxHP}");
    }
    else
    {
        // 1. 先正常移动
        MovePlayer(player, roll);
        messages.Add($"🎲 你走了 {roll} 步，来到了第 {player.Step} 步。");

        // 2. 特殊格触发（金币 / 随机事件 / 战斗，三选一）
        if (GameSpecialCells.GoldSteps.Contains(player.Step))
        {
            string goldMessage = GameEventEngine.TriggerGoldCell(player);
            messages.Add(goldMessage);
        }
        else if (GameSpecialCells.RandomEventSteps.Contains(player.Step))
        {
            var eventResult = GameEventEngine.TriggerRandomEvent(player, players);
            messages.Add(eventResult.Message);
        }
        else if (GameBattleBalance.BattleSteps.Contains(player.Step))
        {
            // 战斗系统：
            // - 玩家先手
            // - 固定伤害公式
            // - 失败回起点并扣金币
            var battleResult = GameBattleEngine.TriggerBattle(player);
            messages.AddRange(battleResult.Logs);
        }

        // 3. 如果战斗失败回起点了，就不要再触发遇见
        if (player.HP > 0 && player.Step != 0)
        {
            string? encounterMessage = GameEventEngine.TryTriggerEncounter(player, players);
            if (!string.IsNullOrWhiteSpace(encounterMessage))
            {
                messages.Add(encounterMessage);
            }
        }

        // 4. 最终状态汇总
        messages.Add($"📍 最终位于第 {player.Step} 步。");
        messages.Add($"🪙 当前金币：{player.Gold}");
        messages.Add($"❤️ 当前生命：{player.HP}/{player.MaxHP}");
    }

    // 5. 统一保存本群玩家
    // 因为随机事件 / 遇见 / 战斗都可能影响别的玩家或当前玩家状态，
    // 所以这里最稳的做法是把本群玩家全部 Update 一次。
    foreach (var p in players)
    {
        GameRepository.UpdatePlayer(p);
    }

    string finalMessage = string.Join("\n", messages);
    await context.AtAsync(context.SenderId, finalMessage);
});

        commandHandler.RegisterCommand("/look", async context =>
        {
            if (!await EnsureGameEnabledAsync(context))
            {
                return;
            }

            var self = GameRepository.GetPlayer(context.PeerId, context.SenderId);
            if (self is null)
            {
                await context.ReplyToMessageAsync("你还没加入游戏。");
                return;
            }

            try
            {
                List<GamePlayer> players = GameRepository.GetPlayers(context.PeerId);
                string base64Image = await GameImageGenerator.GenerateMapAsync(players, MapPath);
                await context.ImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyToMessageAsync($"❌ 地图绘制失败：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/info", async context =>
        {
            if (!await EnsureGameEnabledAsync(context))
            {
                return;
            }

            long targetUserId = ParseMentionedUserId(context.Command) ?? context.SenderId;
            var player = GameRepository.GetPlayer(context.PeerId, targetUserId);

            string displayName = await GroupMemberHelper.GetDisplayNameAsync(
                milky,
                context.PeerId,
                targetUserId,
                targetUserId.ToString());

            if (player is null)
            {
                if (targetUserId == context.SenderId)
                {
                    await context.ReplyToMessageAsync("你还没加入游戏。");
                }
                else
                {
                    await context.ReplyToMessageAsync($"{displayName} 还没加入游戏。");
                }

                return;
            }

            player.Nickname = displayName;
            GameRepository.UpdateNickname(context.PeerId, targetUserId, displayName);

            try
            {
                string base64Image = await GameImageGenerator.GeneratePlayerInfoAsync(player, displayName);
                await context.ImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyToMessageAsync($"❌ 玩家信息图生成失败：{ex.Message}");
            }
        });
    }

    private static async Task<bool> EnsureGameEnabledAsync(CommandContext context)
    {
        if (context.Scene != "group")
        {
            await context.ReplyToMessageAsync("⚠️ 该指令只能在群聊中使用哦！");
            return false;
        }

        if (!GroupConfigManager.IsGameEnabled(context.PeerId))
        {
            return false;
        }

        return true;
    }

    private static long? ParseMentionedUserId(string command)
    {
        var match = MentionRegex.Match(command);
        if (!match.Success)
        {
            return null;
        }

        return long.TryParse(match.Groups[1].Value, out long userId)
            ? userId
            : null;
    }

    private static void MovePlayer(GamePlayer player, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            if (player.Direction >= 0)
            {
                if (player.Step >= GameMapData.MaxStep)
                {
                    player.Direction = -1;
                    player.Step = Math.Max(0, player.Step - 1);
                }
                else
                {
                    player.Step++;
                }
            }
            else
            {
                if (player.Step <= 0)
                {
                    player.Direction = 1;
                    player.Step = Math.Min(GameMapData.MaxStep, player.Step + 1);
                }
                else
                {
                    player.Step--;
                }
            }
        }
    }
    
    
}