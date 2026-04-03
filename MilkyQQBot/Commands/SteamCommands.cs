using System;
using System.Text.RegularExpressions;
using Milky.Net.Client;
using MilkyQQBot.Services;

namespace MilkyQQBot.Commands;

public static class SteamCommands
{
    public static void Register(CommandHandler commandHandler, MilkyClient milky)
    {
        commandHandler.RegisterCommand("/绑定steam", async (context) =>
        {
            var parts = context.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await context.ReplyAsync("格式错误！请使用：/绑定steam 你的SteamID\n例如：/绑定steam 76561198123456789");
                return;
            }

            string inputId = parts[1].Trim();
            string finalSteamId = inputId;

            if (inputId.Length != 17 || !long.TryParse(inputId, out _))
            {
                await context.ReplyAsync($"🔍 检测到输入为自定义链接 [{inputId}]，正在向 G胖 申请解析真实 ID...");
                finalSteamId = await SteamSpyService.ResolveVanityUrlAsync(inputId);

                if (string.IsNullOrEmpty(finalSteamId))
                {
                    await context.ReplyAsync("❌ 绑定失败：无法解析该自定义链接。请检查是否输入正确，或者直接输入 17 位的纯数字 SteamID。");
                    return;
                }
            }

            DatabaseManager.BindSteam(context.SenderId, finalSteamId);

            if (inputId == finalSteamId)
            {
                await context.ReplyAsync($"✅ 绑定成功！你的 QQ 已经与 SteamID [{finalSteamId}] 灵魂互绑！\n以后大家可以通过【/steam查岗】来视奸你的游玩状态了！");
            }
            else
            {
                await context.ReplyAsync($"✅ 解析并绑定成功！\n短链接 [{inputId}] 对应的真实 ID 为：{finalSteamId}\n以后大家可以通过【/steam查岗】来视奸你的游玩状态了！");
            }
        });

        commandHandler.RegisterCommand("/steam查岗", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/steam查岗" && !cmdStr.StartsWith("/steam查岗 "))
                return;

            long targetQqId = ExtractMentionedUserId(cmdStr, context.SenderId);

            string steamId = DatabaseManager.GetSteamId(targetQqId);
            if (string.IsNullOrEmpty(steamId))
            {
                string tips = targetQqId == context.SenderId ? "你还没绑定呢！" : "TA还没绑定呢！";
                await context.ReplyAsync($"❌ 查岗失败：{tips}\n请发送【/绑定steam 你的17位ID，17位ID可通过https://steamid.io/输入用户名进行查询】进行绑定。");
                return;
            }

            await context.ReplyAsync("📡 正在潜入 G胖 的服务器，拉取数据，请稍候...");

            try
            {
                var spyResult = await SteamSpyService.GetFullSpyReportAsync(steamId);
                if (spyResult == null)
                {
                    await context.ReplyAsync("❌ 获取情报失败，可能网络开小差了。");
                    return;
                }

                string base64Image = SteamSpyService.GenerateFullSpyCardBase64(spyResult);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 成功发送查岗与雷达情报：{targetQqId}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 调取数据时发生错误：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/steam游戏库", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/steam游戏库" && !cmdStr.StartsWith("/steam游戏库 "))
                return;

            long targetQqId = ExtractMentionedUserId(cmdStr, context.SenderId);

            string steamId = DatabaseManager.GetSteamId(targetQqId);
            if (string.IsNullOrEmpty(steamId))
            {
                string tips = targetQqId == context.SenderId ? "你还没绑定呢！" : "TA还没绑定呢！";
                await context.ReplyAsync($"❌ 查询失败：{tips}\n请发送【/绑定steam 你的17位ID，17位ID可通过https://steamid.io/输入用户名进行查询】进行绑定。");
                return;
            }

            await context.ReplyAsync("🎮 正在盘点该玩家的赛博资产底裤，请稍候...");

            try
            {
                var libResult = await SteamLibraryService.GetLibraryAsync(steamId);
                if (libResult == null)
                {
                    await context.ReplyAsync("❌ 获取游戏库失败，网络可能开小差了。");
                    return;
                }

                string base64Image = SteamLibraryService.GenerateLibraryCardBase64(libResult);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 成功发送游戏库情报：{targetQqId}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 盘点资产时发生错误：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/steam等级", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/steam等级" && !cmdStr.StartsWith("/steam等级 "))
                return;

            long targetQqId = ExtractMentionedUserId(cmdStr, context.SenderId);

            string steamId = DatabaseManager.GetSteamId(targetQqId);
            if (string.IsNullOrEmpty(steamId))
            {
                string tips = targetQqId == context.SenderId ? "你还没绑定呢！" : "TA还没绑定呢！";
                await context.ReplyAsync($"❌ 查询失败：{tips}\n请发送【/绑定steam 你的17位ID，17位ID可通过https://steamid.io/输入用户名进行查询】进行绑定。");
                return;
            }

            await context.ReplyAsync("💳 正在查询 G胖 的韭菜登记册，请稍候...");

            try
            {
                var levelResult = await SteamLevelService.GetLevelAsync(steamId);
                if (levelResult == null)
                {
                    await context.ReplyAsync("❌ 获取等级信息失败，网络可能开小差了。");
                    return;
                }

                string base64Image = SteamLevelService.GenerateLevelCardBase64(levelResult);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 成功发送 Steam 等级卡片：{targetQqId}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 盘点资产时发生错误：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/csgo库存", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/csgo库存" && !cmdStr.StartsWith("/csgo库存 "))
                return;

            long targetQqId = ExtractMentionedUserId(cmdStr, context.SenderId);

            string steamId = DatabaseManager.GetSteamId(targetQqId);
            if (string.IsNullOrEmpty(steamId))
            {
                string tips = targetQqId == context.SenderId ? "你还没绑定呢！" : "TA还没绑定呢！";
                await context.ReplyAsync($"❌ 查询失败：{tips}\n请发送【/绑定steam 你的17位ID】进行绑定。");
                return;
            }

            await context.ReplyAsync("📦 正在撬开该玩家的仓库卷帘门，搜刮值钱的饰品，请稍候...");

            try
            {
                var invResult = await SteamInventoryService.GetInventoryAsync(steamId);
                if (invResult == null)
                {
                    await context.ReplyAsync("❌ 访问库存失败，可能是接口限流了，请过会儿再试。");
                    return;
                }

                string base64Image = SteamInventoryService.GenerateInventoryCardBase64(invResult);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 成功发送 CSGO 库存：{targetQqId}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 扒底裤时发生错误：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/csgo开箱", async (context) =>
        {
            if (context.Command.Trim() != "/csgo开箱") return;

            await context.ReplyAsync("🎰 正在为您开启【穷鬼专属武器箱】... 正在滚动指针...");
            await Task.Delay(1500);

            try
            {
                var unboxResult = await CsgoUnboxingService.OpenCaseAsync();

                string unboxerName = context.SenderId.ToString();
                if (context.Scene == "group")
                {
                    unboxerName = await GroupMemberHelper.GetDisplayNameAsync(
                        milky,
                        context.PeerId,
                        context.SenderId,
                        unboxerName
                    );
                }

                string base64Image = CsgoUnboxingService.GenerateUnboxImageBase64(unboxResult, unboxerName);
                await context.ReplyImageAsync(base64Image);

                if (unboxResult.IsRare)
                {
                    await Task.Delay(500);
                    string rareTips = unboxResult.ColorHex == "#FFD700"
                        ? $"🚨 高能预警 🚨\n卧槽！本群诞生了一名狗托！[@{context.SenderId}] 居然以 0.26% 的概率开出了 {unboxResult.SkinName}！赶紧让他发红包！"
                        : $"🔥 卧槽出红了 🔥\n恭喜 [@{context.SenderId}] 以 0.64% 的概率斩获隐秘级皮肤：{unboxResult.SkinName}！";

                    await context.ReplyAsync(rareTips);
                }

                Console.WriteLine($"[指令完成] 用户 {context.SenderId} 开箱获得了: {unboxResult.SkinName}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 武器箱卡壳了：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/l4d2", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/l4d2" && !cmdStr.StartsWith("/l4d2 "))
                return;

            long targetQqId = ExtractMentionedUserId(cmdStr, context.SenderId);

            string steamId = DatabaseManager.GetSteamId(targetQqId);
            if (string.IsNullOrEmpty(steamId))
            {
                string tips = targetQqId == context.SenderId ? "你还没绑定呢！" : "TA还没绑定呢！";
                await context.ReplyAsync($"❌ 查询失败：{tips}\n请发送【/绑定steam 你的17位ID或短链接】进行绑定。");
                return;
            }

            await context.ReplyAsync("💉 正在入侵 C.E.D.A. 官方数据库，调取目标体检档案...");

            try
            {
                var l4dResult = await L4d2StatsService.GetStatsAsync(steamId);
                if (l4dResult == null)
                {
                    await context.ReplyAsync("❌ 档案读取失败，可能是网络抽风了。");
                    return;
                }

                string base64Image = L4d2StatsService.GenerateCedaReportBase64(l4dResult);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 成功发送 L4D2 档案卡片，目标：{targetQqId}");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 调取档案时发生异常：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/steamid", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();

            if (!cmdStr.StartsWith("/steamid "))
            {
                await context.ReplyAsync("⚠️ 格式错误！\n请输入：/steamid [你的主页链接 或 自定义后缀]");
                return;
            }

            string input = cmdStr.Substring("/steamid ".Length).Trim();
            await context.ReplyAsync("🔍 正在接入 Steam 数据库，解析你的底层 17 位 ID...");

            string steamId64 = await SteamIdResolverService.ResolveSteamIdAsync(input);

            if (!string.IsNullOrEmpty(steamId64))
            {
                await context.ReplyAsync($"✅ 解析成功！\n你的 17 位 Steam ID 是：\n\n{steamId64}\n\n你可以直接长按复制这串数字，去使用 /绑定steam 指令啦！");
            }
            else
            {
                await context.ReplyAsync("❌ 解析失败：Steam 数据库中找不到该玩家！\n请检查你发的链接或后缀是否拼写正确，或者主页是否设置了完全私密。");
            }
        });
        
        commandHandler.RegisterCommand("/steam热销", async (context) =>
        {
            Console.WriteLine($"[指令触发] 用户 {context.SenderId} 触发了 /steam热销");
            await context.ReplyAsync("🔥 正在抓取 Steam 实时热销榜单，请稍候...");

            try
            {
                var games = await SteamHotService.GetTopSellersAsync();
                if (games.Count == 0)
                {
                    await context.ReplyAsync("❌ 糟糕，访问 Steam 接口失败，G胖可能把网线拔了...");
                    return;
                }

                string base64Image = SteamHotService.GenerateBase64Image(games);
                await context.ReplyImageAsync(base64Image);
                Console.WriteLine($"[指令完成] 已向群 {context.PeerId} 发送 Steam 热销榜。");
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"❌ 生成榜单时发生严重错误：{ex.Message}");
            }
        });
    }

    private static long ExtractMentionedUserId(string cmdStr, long fallbackUserId)
    {
        var match = Regex.Match(cmdStr, @"\[@(\d+)\]");
        return match.Success ? long.Parse(match.Groups[1].Value) : fallbackUserId;
    }
}