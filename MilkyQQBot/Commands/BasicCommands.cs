using System;
using System.Threading.Tasks;
using Milky.Net.Client;
using MilkyQQBot.Services;

namespace MilkyQQBot.Commands;

public static class BasicCommands
{
    public static void Register(CommandHandler commandHandler, MilkyClient milky)
    {
        commandHandler.RegisterCommand("/help", async (context) =>
        {
            Console.WriteLine($"[指令触发] 用户 {context.SenderId} 触发了 /help");

            string helpText = "当前可用指令列表：\n" +
                              "/help - 获取所有指令帮助\n" +
                              "/今日群活跃 - 获取今天群里最活跃的5个人\n" +
                              "/本周群活跃 - 获取近7天群里最活跃的5个人\n" +
                              "/今日词云 - 获取今日词云图\n" +
                              "/群防撤回 - [管理员/群主] 开启或关闭防撤回\n" +
                              "/群ai - [管理员/群主] 开启或关闭群AI自动回复\n" +
                              "/steam热销 - 获取 Steam 实时热销榜前 8 名\n" +
                              "/steamid - [主页链接或后缀] 智能提取17位ID\n" +
                              "！提示: Steam点击顶部【你的名字】->【个人资料】-> 在空白处【右键】-> 选择【复制网页 URL】发送即可\n" +
                              "/绑定steam - 绑定17位SteamID\n" +
                              "/steam查岗 - 查自己，或加 @群友 查别人\n" +
                              "/steam游戏库 - 查游戏底裤和成就(查自己，或加 @群友 查别人)\n" +
                              "/steam等级 - 查看玩家Steam等级与经验(查自己，或加 @群友 查别人)\n" +
                              "/csgo库存 - 偷窥CSGO仓库里最值钱的枪皮(查自己，或加 @群友 查别人)\n" +
                              "/csgo开箱 - 免费体验穷鬼武器箱开奖\n" +
                              "/l4d2 - 查询求生之路2幸存者档案(查自己，或加 @群友 查别人)\n" +
                              "/头像鉴定 - 赛博鉴定头像(鉴定自己，或加 @群友 查别人)\n" +
                              "/头像pk - 赛博头像pk(自己和被@的人pk，或加 @群友A 和 @群友B pk)\n" +
                              "/news - [管理员/群主] 开启或关闭 Telegram 频道订阅";

            try
            {
                string base64ImageUrl = HelpImageGenerator.GenerateBase64Image(helpText);
                await context.ImageAsync(base64ImageUrl);
            }
            catch (Exception ex)
            {
                await context.TextAsync($"❌ 生成说明书失败：{ex.Message}");
            }
        });

        // commandHandler.RegisterCommand("/epic", async (context) =>
        // {
        //     Console.WriteLine($"[指令触发] 用户 {context.SenderId} 触发了 /epic");
        //     await context.TextAsync("⏳ 正在为您生成Epic当前限时免费、即将免费的游戏详情海报，请稍候...");
        //     try
        //     {
        //         await context.ImageAsync(ApiConfig.ApiUrls.EpicFreeGames);
        //     }
        //     catch (Exception ex)
        //     {
        //         await context.TextAsync($"❌ 获取 Epic 数据失败，发生了错误：{ex.Message}");
        //     }
        // });

        // commandHandler.RegisterCommand("/摸鱼日报", async (context) =>
        // {
        //     Console.WriteLine($"[指令触发] 用户 {context.SenderId} 触发了 /摸鱼日报");
        //     await context.TextAsync("🐟 正在为您获取今日份的摸鱼日历，请稍候...");
        //     try
        //     {
        //         await context.ImageAsync(ApiConfig.ApiUrls.MoyuCalendar);
        //     }
        //     catch (Exception ex)
        //     {
        //         await context.TextAsync($"❌ 获取摸鱼日报失败，发生了错误：{ex.Message}");
        //     }
        // });

        commandHandler.RegisterCommand("/今日群活跃", async (context) =>
        {
            if (context.Scene != "group")
            {
                await context.TextAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            Console.WriteLine($"[指令触发] 用户 {context.SenderId} 请求了群 {context.PeerId} 的今日活跃榜");
            var topUsers = DatabaseManager.GetGroupActivity(context.PeerId, DateTime.Today);

            if (topUsers.Count == 0)
            {
                await context.TextAsync("今天群里真冷淡，还没有人讲话");
                return;
            }

            foreach (var user in topUsers)
            {
                user.Nickname = await GroupMemberHelper.GetDisplayNameAsync(
                    milky,
                    context.PeerId,
                    user.SenderId,
                    user.Nickname
                );
            }

            await context.TextAsync("📊 正在统计今日群活跃数据并绘制榜单，请稍候...");
            try
            {
                string title = $"今日群活跃排行榜 ({DateTime.Now:yyyy-MM-dd})";
                string base64ImageUrl = await ActivityImageGenerator.GenerateBase64ImageAsync(topUsers, title);
                await context.ImageAsync(base64ImageUrl);
            }
            catch (Exception ex)
            {
                await context.TextAsync($"❌ 生成排行榜失败，发生了错误：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/本周群活跃", async (context) =>
        {
            if (context.Scene != "group")
            {
                await context.TextAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            Console.WriteLine($"[指令触发] 用户 {context.SenderId} 请求了群 {context.PeerId} 的本周活跃榜");
            var topUsers = DatabaseManager.GetGroupActivity(context.PeerId, DateTime.Today.AddDays(-7));

            if (topUsers.Count == 0)
            {
                await context.TextAsync("这周群里居然一条消息都没有，大家都太高冷了吧！");
                return;
            }

            foreach (var user in topUsers)
            {
                user.Nickname = await GroupMemberHelper.GetDisplayNameAsync(
                    milky,
                    context.PeerId,
                    user.SenderId,
                    user.Nickname
                );
            }

            await context.TextAsync("📊 正在统计近 7 天的数据并生成【本周群活跃榜】，请稍候...");
            try
            {
                string title = "本周群活跃排行榜 (近7天)";
                string base64ImageUrl = await ActivityImageGenerator.GenerateBase64ImageAsync(topUsers, title);
                await context.ImageAsync(base64ImageUrl);
            }
            catch (Exception ex)
            {
                await context.TextAsync($"❌ 生成周榜失败：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/今日词云", async (context) =>
        {
            if (context.Scene != "group")
            {
                await context.TextAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            Console.WriteLine($"[指令触发] 用户 {context.SenderId} 请求了今日词云");
            var messages = DatabaseManager.GetGroupMessagesText(context.PeerId, DateTime.Today);

            if (messages.Count == 0)
            {
                await context.TextAsync("今天群里太安静了，连一句话都没有，生成不了词云呢~");
                return;
            }

            await context.TextAsync("☁️ 正在进行语义分析并渲染今日词云，请稍候...");
            try
            {
                string title = $"今日群聊关键词 ({DateTime.Now:yyyy-MM-dd})";
                string base64ImageUrl = await WordCloudGenerator.GenerateAsync(messages, title);

                if (base64ImageUrl != null)
                {
                    await context.ImageAsync(base64ImageUrl);
                }
                else
                {
                    await context.TextAsync("群里大家发的话太短或者全是被过滤掉的水词，提取不到有效关键词哦！");
                }
            }
            catch (Exception ex)
            {
                await context.TextAsync($"❌ 生成词云失败：{ex.Message}");
            }
        });

        commandHandler.RegisterCommand("/群防撤回", async (context) =>
        {
            if (context.Scene != "group") return;

            if (context.SenderRole != "Owner" && context.SenderRole != "Admin")
            {
                await context.TextAsync("⚠️ 权限不足！该指令仅限群主或管理员使用。");
                return;
            }

            bool isNowEnabled = GroupConfigManager.ToggleAntiRecall(context.PeerId);

            if (isNowEnabled)
            {
                await context.TextAsync("✅ 已开启群防撤回。群员撤回消息将被机器人公开处刑！");
            }
            else
            {
                await context.TextAsync("❌ 已关闭群防撤回。");
            }
        });

        commandHandler.RegisterCommand("/群ai", async (context) =>
        {
            if (context.Scene != "group") return;

            if (context.SenderRole != "Owner" && context.SenderRole != "Admin")
            {
                await context.TextAsync("⚠️ 权限不足！该指令仅限群主或管理员使用。");
                return;
            }

            bool isNowEnabled = GroupConfigManager.ToggleAiChat(context.PeerId);

            if (isNowEnabled)
            {
                await context.TextAsync("✅ 已开启本群 AI 阴阳怪气功能。");
            }
            else
            {
                await context.TextAsync("❌ 已关闭本群 AI 聊天功能。");
            }
        });
        
        commandHandler.RegisterCommand("/news", async (context) =>
        {
            if (context.Scene != "group")
            {
                await context.TextAsync("⚠️ 该指令只能在群聊中使用哦！");
                return;
            }

            if (context.SenderRole != "Owner" && context.SenderRole != "Admin")
            {
                await context.TextAsync("⚠️ 权限不足！该指令仅限群主或管理员使用。");
                return;
            }

            bool isNowEnabled = GroupConfigManager.ToggleTelegramNews(context.PeerId);

            if (isNowEnabled)
            {
                await context.TextAsync("✅ 已开启 tg 频道订阅。后续频道有新消息时，机器人会自动同步到本群。");
            }
            else
            {
                await context.TextAsync("❌ 已关闭 Telegram 频道订阅。");
            }
        });
        
        // ==========================================
        // 核心查询指令：/find (仅限私聊可用)
        // 格式：/find 3338008104 或者 /find @12345678
        // ==========================================
        commandHandler.RegisterCommand("/find", async (context) =>
        {
            string cmdStr = context.Command.TrimEnd(); 
            if (!cmdStr.StartsWith("/find ")) return;

            // 【新增】安全限制：绝对禁止在群聊中使用，避免隐私泄露和炸群
            if (context.Scene != "friend") return;

            // 1. 鉴权逻辑
            long ownerId = AppConfig.Current.Bot.OwnerId;
            bool isOwner = (context.SenderId == ownerId);
            int remainingCount = 0;

            if (!isOwner)
            {
                remainingCount = DatabaseManager.GetRemainingQueryCount(context.SenderId);
                if (remainingCount <= 0)
                {
                    await context.TextAsync($"⛔ 权限不足或额度已耗尽！\n这是一个受限的高级查询接口，请私聊机器人主人 (QQ: {ownerId}) 申请查询权限及额度。");
                    return;
                }
            }

            // 2. 获取查询目标
            string target = cmdStr.Substring("/find ".Length).Trim();
            if (string.IsNullOrEmpty(target))
            {
                await context.TextAsync("⚠️ 缺少查询目标！\n格式：/find [QQ/手机号/@微博uid]");
                return;
            }

            await context.TextAsync($"⏳ 奈奈川正在潜入深层数据网，检索 [{target}] 的情报，请稍候...");

            // 3. 执行查询
            string queryResult = await PrivacyQueryService.SearchAsync(target);

            // 4. 扣除额度并拼接提示词 (主人无限次，不扣除)
            if (!isOwner)
            {
                DatabaseManager.ConsumeQueryPermission(context.SenderId);
                int left = DatabaseManager.GetRemainingQueryCount(context.SenderId);
                queryResult += $"\n\n[提示：本次查询扣除 1 点额度，您当前剩余额度：{left} 次]";
            }
            else
            {
                queryResult += $"\n\n[提示：最高权限者 (主人) 豁免额度扣除]";
            }

            // 5. 输出结果
            await context.TextAsync(queryResult);
        });
        
        // ==========================================
        // 接码系统：/接码项目 [搜索词] (仅限主人私聊)
        // ==========================================
        commandHandler.RegisterCommand("/接码项目", async (context) =>
        {
            if (context.Scene != "friend") return;
            
            // ⚠️ 安全锁：请替换为你的真实 QQ 号，防止别人私聊机器人把你的钱扣光！
            if (context.SenderId != 3338008104) return; 

            string cmdStr = context.Command.TrimEnd();
            string keyword = cmdStr.Replace("/接码项目", "").Trim();

            if (string.IsNullOrEmpty(keyword))
            {
                await context.TextAsync("⚠️ 格式错误！请输入：/接码项目 [项目关键词]\n例如：/接码项目 抖音");
                return;
            }

            await context.TextAsync($"⏳ 正在云端检索包含[{keyword}]的项目，请稍候...");
            string result = await SmsService.SearchProjectAsync(keyword);
            await context.TextAsync(result);
        });

        // ==========================================
        // 接码系统：/取号 [项目ID] (仅限主人私聊)
        // ==========================================
        commandHandler.RegisterCommand("/取号", async (context) =>
        {
            if (context.Scene != "friend") return;
            if (context.SenderId != 3338008104) return; 

            string cmdStr = context.Command.TrimEnd();
            string projectId = cmdStr.Replace("/取号", "").Trim();

            if (string.IsNullOrEmpty(projectId))
            {
                await context.TextAsync("⚠️ 格式错误！请输入：/取号 [项目ID]\n例如：/取号 1001");
                return;
            }

            await context.TextAsync($"📡 正在向平台申请项目 [{projectId}] 的手机号...");
            string result = await SmsService.GetPhoneNumberAsync(projectId);
            await context.TextAsync(result);
        });

        // ==========================================
        // 接码系统：/拿码 [项目ID] [手机号] (仅限主人私聊)
        // ==========================================
        commandHandler.RegisterCommand("/拿码", async (context) =>
        {
            if (context.Scene != "friend") return;
            if (context.SenderId != 3338008104) return; 

            var parts = context.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                await context.TextAsync("⚠️ 格式错误！请输入：/拿码 [项目ID] [手机号]\n例如：/拿码 1001 17012345678");
                return;
            }

            string projectId = parts[1];
            string phone = parts[2];

            await context.TextAsync("📩 正在拦截短信中心，尝试读取验证码...");
            string result = await SmsService.GetVerifyCodeAsync(projectId, phone);
            await context.TextAsync(result);
        });
        

    }
}