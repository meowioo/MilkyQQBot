using System.Collections.Concurrent;
using System.Text.Json;
using System.IO;
using System;

namespace MilkyQQBot;

// 群功能配置实体类
public class GroupFeatureConfig
{
    // 防撤回开关，默认关闭
    public bool AntiRecallEnabled { get; set; } = false;
    
    // 群 AI 聊天开关，默认关闭
    public bool AiChatEnabled { get; set; } = false; 
}

public static class GroupConfigManager
{
    private const string ConfigPath = "group_configs.json";
    
    private static ConcurrentDictionary<long, GroupFeatureConfig> _configs = new();
    
    // 【核心修复】引入文件读写排他锁，防止高并发下触发 IOException
    private static readonly object _fileLock = new();

    public static void Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                // 读取时也加锁，防止读取瞬间正好有其他线程在写入
                lock (_fileLock)
                {
                    var json = File.ReadAllText(ConfigPath);
                    _configs = JsonSerializer.Deserialize<ConcurrentDictionary<long, GroupFeatureConfig>>(json) ?? new();
                }
                Console.WriteLine("[系统] 群配置文件加载成功。");
            }
            catch (Exception ex) 
            { 
                // 【优化】不要吞掉错误信息，打印出来方便排查是否是 JSON 格式损坏
                Console.WriteLine($"[系统] 群配置文件解析失败，将使用默认配置。原因: {ex.Message}"); 
            }
        }
    }

    public static void Save()
    {
        try
        {
            // 序列化可以在锁外面做，不影响性能
            var json = JsonSerializer.Serialize(_configs, new JsonSerializerOptions { WriteIndented = true });
            
            // 【核心修复】加锁，确保同一时刻绝对只有一个线程能执行写入操作
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
}