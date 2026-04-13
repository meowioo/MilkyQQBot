using Milky.Net.Client;
using MilkyQQBot;
using MilkyQQBot.Commands;
using MilkyQQBot.Events;
using MilkyQQBot.Game;
using MilkyQQBot.Services;

Console.WriteLine("正在初始化 Milky 机器人客户端...");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Console.WriteLine("[Fatal] 未处理异常:");
    Console.WriteLine(e.ExceptionObject?.ToString());
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Console.WriteLine("[Task] 未观察到的任务异常:");
    Console.WriteLine(e.Exception.ToString());
    e.SetObserved();
};


// 初始化数据库与群配置
DatabaseManager.Initialize();
GameRepository.Initialize();
GroupConfigManager.Load();

// 从配置读取
var botConfig = AppConfig.Current.Bot;

// 准备一个 HttpClient 实例
HttpClient client = new()
{
    BaseAddress = new Uri(botConfig.WebSocketUrl),
};

// 创建 MilkyClient 实例
MilkyClient milky = new(client);

// 运行时状态
var state = new BotRuntimeState();

// 指令管理器
CommandHandler commandHandler = new();

// 注册命令
BasicCommands.Register(commandHandler, milky);
SteamCommands.Register(commandHandler, milky);
FunCommands.Register(commandHandler, milky, state);
GameCommands.Register(commandHandler, milky);

// 注册事件
BotEventRegistrar.Register(milky, commandHandler, state);

try
{
    await TelegramMsgService.StartAsync(milky);
}
catch (Exception ex)
{
    Console.WriteLine("[Program] TelegramMsgService 启动时发生未处理异常，但主程序将继续运行。");
    Console.WriteLine(ex);
}

try
{
    var result = await milky.System.GetImplInfoAsync();
    Console.WriteLine($"[HTTP API 测试成功] 服务端信息: {result}");
}
catch (Exception ex)
{
    Console.WriteLine($"[HTTP API 测试失败] {ex.Message}");
}

Console.WriteLine("正在尝试连接 WebSocket...");
_ = Task.Run(async () =>
{
    try
    {
        await milky.ReceivingEventUsingWebSocketAsync();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[WebSocket 发生严重错误] 监听中断: {ex.Message}");
        Console.ResetColor();
    }
});

Console.WriteLine("初始化完成，挂起程序等待消息...");
Console.WriteLine("------------------------------------------------------------------------");
Console.WriteLine("------------------------------------------------------------------------");

await Task.Delay(-1);