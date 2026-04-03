using Milky.Net.Client;
using Milky.Net.Model;

namespace MilkyQQBot;

public static class BotScheduler
{
    public static void StartMorningNightRoutine(MilkyClient client, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("[系统] 已启动全局定时早晚安任务 (精准休眠模式)，将广播至所有已加入的群聊！");

        _ = Task.Run(async () =>
        {
            // 【核心修改】把 true 换成了检查令牌状态
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    
                    var targetMorning = now.Date.AddHours(8);
                    var targetNight = now.Date.AddHours(23).AddMinutes(30);

                    if (now >= targetMorning) targetMorning = targetMorning.AddDays(1);
                    if (now >= targetNight) targetNight = targetNight.AddDays(1);

                    var nextTarget = targetMorning < targetNight ? targetMorning : targetNight;
                    var delay = nextTarget - now;

                    Console.WriteLine($"[定时任务] 下一次广播时间: {nextTarget:yyyy-MM-dd HH:mm:ss}");
                    Console.WriteLine($"[定时任务] 线程将完美休眠 {delay.TotalHours:F2} 小时，期间 0 性能消耗...");

                    // 传入取消令牌，让它即使在“睡梦中”也能被外部叫醒并终止
                    await Task.Delay(delay, cancellationToken);

                    Console.WriteLine($"[定时任务] 时间到！({DateTime.Now:HH:mm})，正在获取群列表并广播...");

                    var getGroupReq = new GetGroupListRequest();
                    var groupResponse = await client.System.GetGroupListAsync(getGroupReq); 

                    string imageUrl = ApiConfig.ApiUrls.MorningNightImage;
                    var segment = new OutgoingSegment<ImageOutgoingSegmentData>(new ImageOutgoingSegmentData(new MilkyUri(imageUrl), null));
                    var segments = new OutgoingSegment[] { segment };

                    foreach (var group in groupResponse.Groups) 
                    {
                        try
                        {
                            long targetGroupId = group.GroupId; 
                            var req = new SendGroupMessageRequest(targetGroupId, segments);
                            await client.Message.SendGroupMessageAsync(req);
                            
                            Console.WriteLine($"[定时任务] 成功发送至群: {targetGroupId}");
                            
                            await Task.Delay(1500, cancellationToken); 
                        }
                        catch (Exception innerEx)
                        {
                            Console.WriteLine($"[定时发送警告] 发送至群 {group.GroupId} 失败: {innerEx.Message}");
                        }
                    }

                    Console.WriteLine("[定时任务] 本次所有群聊早晚安广播完毕！");
                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    // 如果收到取消信号，Task.Delay 会抛出这个异常，我们捕获它并优雅退出循环
                    Console.WriteLine("[定时任务] 收到停止信号，后台任务已安全退出。");
                    break;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[定时任务全局报错] 任务崩溃: {ex.Message}");
                    Console.ResetColor();
                    
                    // 等待重试时也受令牌控制
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                }
            }
        }, cancellationToken);
    }
    
}