namespace MilkyQQBot.Features.ChatAi.V2.Vision;

/// <summary>
/// 图片摘要后台 worker。
/// 启动后会一直轮询数据库里的 pending 图片任务，
/// 调视觉模型生成摘要，再写回数据库。
/// </summary>
public static class ImageSummaryWorker
{
    private static CancellationTokenSource? _cts;
    private static Task? _workerTask;
    private static bool _started;

    /// <summary>
    /// 启动后台 worker。
    /// 这个方法只会真正启动一次，多次调用也不会重复开线程。
    /// </summary>
    public static void Start(IImageSummaryProvider provider)
    {
        if (_started)
            return;

        _started = true;
        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => RunLoopAsync(provider, _cts.Token));
    }

    /// <summary>
    /// 停止后台 worker。
    /// </summary>
    public static async Task StopAsync()
    {
        if (_cts == null)
            return;

        _cts.Cancel();

        if (_workerTask != null)
        {
            try
            {
                await _workerTask;
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需要额外处理
            }
        }

        _started = false;
        _cts.Dispose();
        _cts = null;
        _workerTask = null;
    }

    /// <summary>
    /// 主循环：
    /// 1. 拉取 pending 图片
    /// 2. 抢占为 running
    /// 3. 生成摘要
    /// 4. 成功写回 / 失败标记
    /// </summary>
    private static async Task RunLoopAsync(
        IImageSummaryProvider provider,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pendingUrls = DatabaseManager.GetPendingImageSummaryUrls(3);

                // 没任务时稍微睡久一点
                if (pendingUrls.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
                    continue;
                }

                foreach (var imageUrl in pendingUrls)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 抢占任务，防止重复处理
                    bool claimed = DatabaseManager.TryMarkImageSummaryRunning(imageUrl);
                    if (!claimed)
                        continue;

                    try
                    {
                        string? summary = await provider.GenerateSummaryAsync(imageUrl, cancellationToken);

                        if (string.IsNullOrWhiteSpace(summary))
                        {
                            DatabaseManager.MarkImageSummaryFailed(imageUrl);
                            Console.WriteLine($"[图片摘要失败] 结果为空: {imageUrl}");
                            continue;
                        }

                        DatabaseManager.SaveImageSummary(imageUrl, summary);
                        Console.WriteLine($"[图片摘要完成] {summary} <- {imageUrl}");
                    }
                    catch (Exception ex)
                    {
                        DatabaseManager.MarkImageSummaryFailed(imageUrl);
                        Console.WriteLine($"[图片摘要异常] {ex.Message}");
                    }
                }

                // 有任务时短暂停一下，避免空转太快
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 正常退出
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[图片摘要Worker异常] {ex.Message}");

                // 避免异常时疯狂重试刷日志
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
}