using System;
using System.IO;
using System.Text.Json;

namespace MilkyQQBot;

public class AppConfigRoot
{
    public BotConfig Bot { get; set; } = new();
    public SteamConfig Steam { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
    public SmsConfig Sms { get; set; } = new();
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