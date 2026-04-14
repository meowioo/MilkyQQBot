using System;
using System.Collections.Concurrent;
using MilkyQQBot.Features.ChatAi.V2.Models;

namespace MilkyQQBot.Services;

public class BotRuntimeState
{
    // 记录每个群当前是否正在等待 AI 返回结果（防并发锁）
    public ConcurrentDictionary<long, bool> GroupAiThinkingStatus { get; } = new();

    // 记录每个群上一次成功发送 AI 回复的时间（冷却记录）
    public ConcurrentDictionary<long, DateTime> GroupAiLastReplyTime { get; } = new();
    
    public Dictionary<long, GroupConversationState> GroupConversationStates { get; set; } = new();

    // 记录每个群当前是否正在进行头像鉴定（防群内多点并发冲突）
    public ConcurrentDictionary<long, bool> GroupPhysiognomyStatus { get; } = new();

    // 记录每个群当前的决斗状态。Key: 群号, Value: 正在决斗的两人名字
    public ConcurrentDictionary<long, string> GroupPkStatus { get; } = new();
}