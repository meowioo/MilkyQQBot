namespace MilkyQQBot.Features.ChatAi.V2.Vision;

/// <summary>
/// 图片摘要提供器接口。
/// 后台 worker 不关心你具体用哪个视觉模型，只负责拿 URL、要摘要。
/// </summary>
public interface IImageSummaryProvider
{
    /// <summary>
    /// 根据图片 URL 生成一句很短的中文摘要。
    /// 例如：猫咪表情包 / 聊天截图 / 游戏结算图
    /// </summary>
    Task<string?> GenerateSummaryAsync(string imageUrl, CancellationToken cancellationToken = default);
}