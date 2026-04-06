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
                await context.ReplyReplyAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            try
            {
                string base64Image = await GameImageGenerator.GenerateHelpMenuAsync();
                await context.ReplyImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyReplyAsync($"❌ 帮助菜单生成失败：{ex.Message}");
            }
        });
        
        commandHandler.RegisterCommand("/game", async context =>
        {
            if (context.Scene != "group")
            {
                await context.ReplyReplyAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            if (context.SenderRole != "Owner" && context.SenderRole != "Admin")
            {
                await context.ReplyReplyAsync("⚠️ 权限不足！该指令仅限群主或管理员使用。");
                return;
            }

            bool isEnabled = GroupConfigManager.ToggleGame(context.PeerId);
            if (isEnabled)
            {
                await context.ReplyReplyAsync("✅ 已开启本群冒险游戏！现在可以使用 /join /go /look /info");
            }
            else
            {
                await context.ReplyReplyAsync("❌ 已关闭本群冒险游戏。");
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
                await context.ReplyReplyAsync($"✅ {displayName} 加入成功，当前位置：第 0 步。");
            }
            else
            {
                var player = GameRepository.GetPlayer(context.PeerId, context.SenderId);
                int step = player?.Step ?? 0;
                await context.ReplyReplyAsync($"ℹ️ 你已经加入过游戏了，当前在第 {step} 步。");
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
                await context.ReplyReplyAsync("你还没加入游戏。");
                return;
            }

            string displayName = await GroupMemberHelper.GetDisplayNameAsync(
                milky,
                context.PeerId,
                context.SenderId,
                player.Nickname);

            player.Nickname = displayName;

            int roll = Random.Shared.Next(0, 5);

            var messages = new List<string>();

            if (roll == 0)
            {
                messages.Add("🎲 你走了 0 步，原地摸鱼了一会儿。");
                messages.Add($"📍 当前位于第 {player.Step} 步。");
                messages.Add($"🪙 当前金币：{player.Gold}");
            }
            else
            {
                // 1. 先正常移动
                MovePlayer(player, roll);
                messages.Add($"🎲 你走了 {roll} 步，来到了第 {player.Step} 步。");

                // 2. 金币格 / 随机事件格（二选一）
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

                // 3. 事件结算后，再检查遇见
                string? encounterMessage = GameEventEngine.TryTriggerEncounter(player, players);
                if (!string.IsNullOrWhiteSpace(encounterMessage))
                {
                    messages.Add(encounterMessage);
                }

                // 4. 最终状态汇总
                messages.Add($"📍 最终位于第 {player.Step} 步。");
                messages.Add($"🪙 当前金币：{player.Gold}");
            }

            // 5. 保存
            // 为了简单稳定，直接保存本群所有玩家，后续你觉得有必要再优化为只保存变更玩家
            foreach (var p in players)
            {
                GameRepository.UpdatePlayer(p);
            }

            string finalMessage = string.Join("\n", messages);
            await context.SendMentionTextAsync(context.SenderId, finalMessage);
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
                await context.ReplyReplyAsync("你还没加入游戏。");
                return;
            }

            try
            {
                List<GamePlayer> players = GameRepository.GetPlayers(context.PeerId);
                string base64Image = await GameImageGenerator.GenerateMapAsync(players, MapPath);
                await context.ReplyImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyReplyAsync($"❌ 地图绘制失败：{ex.Message}");
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
                    await context.ReplyReplyAsync("你还没加入游戏。");
                }
                else
                {
                    await context.ReplyReplyAsync($"{displayName} 还没加入游戏。");
                }

                return;
            }

            player.Nickname = displayName;
            GameRepository.UpdateNickname(context.PeerId, targetUserId, displayName);

            try
            {
                string base64Image = await GameImageGenerator.GeneratePlayerInfoAsync(player, displayName);
                await context.ReplyImageAsync(base64Image);
            }
            catch (Exception ex)
            {
                await context.ReplyReplyAsync($"❌ 玩家信息图生成失败：{ex.Message}");
            }
        });
    }

    private static async Task<bool> EnsureGameEnabledAsync(CommandContext context)
    {
        if (context.Scene != "group")
        {
            await context.ReplyReplyAsync("⚠️ 该指令只能在群聊中使用哦！");
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