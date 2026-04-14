using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MilkyQQBot;

public class AppConfigRoot
{
    public BotConfig Bot { get; set; } = new();
    public SteamConfig Steam { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
    public SmsConfig Sms { get; set; } = new();
    public TelegramConfig Telegram { get; set; } = new();
}

public class BotConfig
{
    public string WebSocketUrl { get; set; } = "ws://localhost:3010/";
    public long OwnerId { get; set; }
    public long BotId { get; set; }
}

public class SteamConfig
{
    public string ApiKey { get; set; } = "";
}

public class AiConfig
{
    public AiProviderConfig Chat { get; set; } = new();
    public AiProviderConfig Physiognomy { get; set; } = new();
    public AiProviderConfig AvatarPk { get; set; } = new();
}

public class AiProviderConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public class SmsConfig
{
    public string ApiUrl { get; set; } = "";
    public string Token { get; set; } = "";
}

public class TelegramConfig
{
    public bool Enabled { get; set; } = true;

    // true = 首次启动只建立基线，不补发历史
    public bool BootstrapWithoutPush { get; set; } = true;

    // 是否在推送文本前加来源头部
    public bool IncludeSourceHeader { get; set; } = true;

    // 是否显示发送者
    public bool IncludeSenderName { get; set; } = true;

    // Telegram API 凭据
    public string ApiId { get; set; } = "";
    public string ApiHash { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string Password { get; set; } = "";

    // session 文件
    public string SessionPath { get; set; } = "WTelegram.session";

    // Telegram 媒体缓存目录
    public string MediaCacheDirectory { get; set; } = "telegram_media";

    // 媒体文件保留小时数
    public int MediaKeepHours { get; set; } = 12;
    // 是否启用文本关键字过滤
    public bool EnableKeywordFilter { get; set; } = true;

    // 额外的自定义屏蔽关键字
    public List<string> BlockedKeywords { get; set; } = new();

    // 监听源，后续可同时支持频道、群组、私聊
    public List<TelegramSourceConfig> Sources { get; set; } = new();
    
    // 多久清理一次缓存
    public int MediaCleanupIntervalMinutes { get; set; } = 60;

    // 是否输出 WTelegram 原始底层日志
    public bool VerboseSdkLog { get; set; } = false;

    // 是否打印“成功推送到群”的提示
    public bool LogForwardSuccess { get; set; } = true;
}

public class TelegramSourceConfig
{
    // channel / group / user
    public string Type { get; set; } = "channel";

    // 支持 @username、标题名、纯数字 id
    public string Value { get; set; } = "";

    // 给 QQ 侧显示的别名，可为空
    public string Alias { get; set; } = "";
}


public static class AppConfig
{
    private static readonly Lazy<AppConfigRoot> _current = new(LoadInternal);

    public static AppConfigRoot Current => _current.Value;

    private static AppConfigRoot LoadInternal()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        if (!File.Exists(path))
            throw new FileNotFoundException($"未找到配置文件: {path}");

        string json = File.ReadAllText(path);

        var config = JsonSerializer.Deserialize<AppConfigRoot>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (config == null)
            throw new InvalidOperationException("appsettings.json 解析失败");

        return config;
    }
}