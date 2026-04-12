using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MilkyQQBot;

// 群功能配置实体类
public class GroupFeatureConfig
{
    // 防撤回开关，默认关闭
    public bool AntiRecallEnabled { get; set; } = false;

    // 群 AI 聊天开关，默认关闭
    public bool AiChatEnabled { get; set; } = false;

    public bool GameEnabled { get; set; } = false;

    // Telegram 频道订阅开关，默认关闭
    public bool TelegramNewsEnabled { get; set; } = false;
}

public static class GroupConfigManager
{
    private const string ConfigPath = "group_configs.json";
    private static ConcurrentDictionary<long, GroupFeatureConfig> _configs = new();

    // 文件读写排他锁
    private static readonly object _fileLock = new();

    public static void Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                lock (_fileLock)
                {
                    var json = File.ReadAllText(ConfigPath);
                    _configs = JsonSerializer.Deserialize<ConcurrentDictionary<long, GroupFeatureConfig>>(json) ?? new();
                }

                Console.WriteLine("[系统] 群配置文件加载成功。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[系统] 群配置文件解析失败，将使用默认配置。原因: {ex.Message}");
            }
        }
    }

    public static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            lock (_fileLock)
            {
                File.WriteAllText(ConfigPath, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[系统警告] 保存群配置失败: {ex.Message}");
        }
    }

    public static GroupFeatureConfig GetConfig(long groupId)
    {
        return _configs.GetOrAdd(groupId, _ => new GroupFeatureConfig());
    }

    public static bool ToggleAntiRecall(long groupId)
    {
        var config = GetConfig(groupId);
        config.AntiRecallEnabled = !config.AntiRecallEnabled;
        Save();
        return config.AntiRecallEnabled;
    }

    public static bool ToggleAiChat(long groupId)
    {
        var config = GetConfig(groupId);
        config.AiChatEnabled = !config.AiChatEnabled;
        Save();
        return config.AiChatEnabled;
    }

    public static bool IsGameEnabled(long groupId)
    {
        return GetConfig(groupId).GameEnabled;
    }

    public static bool ToggleGame(long groupId)
    {
        var config = GetConfig(groupId);
        config.GameEnabled = !config.GameEnabled;
        Save();
        return config.GameEnabled;
    }

    public static bool ToggleTelegramNews(long groupId)
    {
        var config = GetConfig(groupId);
        config.TelegramNewsEnabled = !config.TelegramNewsEnabled;
        Save();
        return config.TelegramNewsEnabled;
    }

    public static IReadOnlyCollection<long> GetTelegramNewsEnabledGroupIds()
    {
        return _configs
            .Where(x => x.Value.TelegramNewsEnabled)
            .Select(x => x.Key)
            .ToArray();
    }
}