using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MilkyQQBot;

// 群功能配置实体类
public class GroupFeatureConfig
{
    public bool AntiRecallEnabled { get; set; } = false;
    public bool AiChatEnabled { get; set; } = false;
    public bool GameEnabled { get; set; } = false;

    // 替换旧 TelegramNewsEnabled
    public bool TelegramMsgEnabled { get; set; } = false;
}

public static class GroupConfigManager
{
    private const string ConfigPath = "group_configs.json";
    private static ConcurrentDictionary<long, GroupFeatureConfig> _configs = new();
    private static readonly object _fileLock = new();

    public static void Load()
    {
        if (!File.Exists(ConfigPath))
            return;

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

    public static bool ToggleTelegramMsg(long groupId)
    {
        var config = GetConfig(groupId);
        config.TelegramMsgEnabled = !config.TelegramMsgEnabled;
        Save();
        return config.TelegramMsgEnabled;
    }

    public static IReadOnlyCollection<long> GetTelegramMsgEnabledGroupIds()
    {
        return _configs
            .Where(x => x.Value.TelegramMsgEnabled)
            .Select(x => x.Key)
            .ToArray();
    }
}