using System;
using System.Text.RegularExpressions;
using Milky.Net.Client;
using MilkyQQBot.Services;

namespace MilkyQQBot.Commands;

public static class FunCommands
{
    public static void Register(CommandHandler commandHandler, MilkyClient milky, BotRuntimeState state)
    {
        commandHandler.RegisterCommand("/头像鉴定", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (cmdStr != "/头像鉴定" && !cmdStr.StartsWith("/头像鉴定 "))
                return;

            if (context.Scene == "group")
            {
                if (state.GroupPhysiognomyStatus.TryGetValue(context.PeerId, out bool isWorking) && isWorking)
                {
                    await context.TextAsync("⚠️ 奈奈川正在为上一位患者看诊，请排队等待上一个人鉴定完毕！");
                    return;
                }

                state.GroupPhysiognomyStatus[context.PeerId] = true;
            }

            Console.WriteLine($"\n---> [鉴定系统] 开始处理群 {context.PeerId} 用户 {context.SenderId} 的请求");

            try
            {
                long targetQqId = context.SenderId;
                string targetNickname = context.SenderId.ToString();

                var match = Regex.Match(cmdStr, @"\[@(\d+)\]");
                if (match.Success)
                {
                    targetQqId = long.Parse(match.Groups[1].Value);
                    Console.WriteLine($"[鉴定系统] 检测到目标为被艾特用户: {targetQqId}");
                }

                if (context.Scene == "group")
                {
                    targetNickname = await GroupMemberHelper.GetDisplayNameAsync(
                        milky,
                        context.PeerId,
                        targetQqId,
                        targetNickname
                    );
                }

                string tips = targetQqId == context.SenderId ? "你" : "TA";
                await context.TextAsync($"🔮 奈奈川正在凝视{tips}的头像，正在手写诊断报告单，请稍候...");

                Console.WriteLine($"[鉴定系统] 准备调用 API，目标QQ: {targetQqId}, 昵称: {targetNickname}");

                string result = await CyberPhysiognomyService.GenerateReportImageBase64Async(targetQqId.ToString(), targetNickname);

                if (result.StartsWith("base64://"))
                {
                    await context.ImageAsync(result);
                    Console.WriteLine("[鉴定系统] ✅ 成功发送鉴定图片！");
                }
                else
                {
                    await context.TextAsync(result);
                    Console.WriteLine($"[鉴定系统] ⚠️ 发送了文字错误提示: {result}");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[鉴定系统-严重崩溃] 发生了未捕获的异常：");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();

                await context.TextAsync("❌ 糟糕，大师在凝视深渊的时候，被深渊反噬了（程序内部错误，已记录日志）。");
            }
            finally
            {
                if (context.Scene == "group")
                {
                    state.GroupPhysiognomyStatus[context.PeerId] = false;
                }

                Console.WriteLine("---> [鉴定系统] 任务执行结束。\n");
            }
        });

        commandHandler.RegisterCommand("/头像pk", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd();
            if (!cmdStr.StartsWith("/头像pk")) return;

            if (context.Scene != "group")
            {
                await context.TextAsync("⚠️ 电子斗蛐蛐只能在群聊中进行哦！");
                return;
            }

            var matches = Regex.Matches(cmdStr, @"\[@(\d+)\]");
            if (matches.Count == 0)
            {
                await context.TextAsync("⚠️ 你得先 @人 才能开打！\n用法：/头像pk @某人\n或者：/头像pk @甲 @乙");
                return;
            }

            long targetA_Id;
            long targetB_Id;

            if (matches.Count == 1)
            {
                targetA_Id = context.SenderId;
                targetB_Id = long.Parse(matches[0].Groups[1].Value);
            }
            else
            {
                targetA_Id = long.Parse(matches[0].Groups[1].Value);
                targetB_Id = long.Parse(matches[1].Groups[1].Value);
            }

            if (targetA_Id == targetB_Id)
            {
                await context.TextAsync("⚠️ 精神分裂？不能和自己决斗哦！");
                return;
            }

            if (state.GroupPkStatus.TryGetValue(context.PeerId, out string fightingNames) && !string.IsNullOrEmpty(fightingNames))
            {
                await context.TextAsync($"⚠️ 擂台已被占用！【{fightingNames}】正在激烈交战中，请等待他们决出胜负！");
                return;
            }

            state.GroupPkStatus[context.PeerId] = $"{targetA_Id} 和 {targetB_Id}";

            try
            {
                string nameA = await GroupMemberHelper.GetDisplayNameAsync(
                    milky,
                    context.PeerId,
                    targetA_Id,
                    targetA_Id.ToString()
                );

                string nameB = await GroupMemberHelper.GetDisplayNameAsync(
                    milky,
                    context.PeerId,
                    targetB_Id,
                    targetB_Id.ToString()
                );

                state.GroupPkStatus[context.PeerId] = $"{nameA} 和 {nameB}";

                await context.TextAsync($"⚔️ 擂台钟声敲响！\n【{nameA}】与【{nameB}】已被丢进斗技场！\n奈奈川正在为您生成赛博战斗日志，请稍候...");

                string result = await AvatarPkService.GeneratePkImageBase64Async(
                    targetA_Id.ToString(),
                    nameA,
                    targetB_Id.ToString(),
                    nameB
                );

                if (result.StartsWith("base64://"))
                {
                    await context.ImageAsync(result);
                }
                else
                {
                    await context.TextAsync(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[斗蛐蛐崩溃] {ex.Message}");
                await context.TextAsync("❌ 决斗擂台被陨石砸中了（程序内部错误）。");
            }
            finally
            {
                state.GroupPkStatus.TryRemove(context.PeerId, out _);
            }
        });
    }
}