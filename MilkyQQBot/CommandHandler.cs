namespace MilkyQQBot;

public class CommandHandler
{
    // 用于存储 "指令名称" -> "对应的处理方法" 的字典
    private readonly Dictionary<string, Func<CommandContext, Task>> _commands = new();

    // 注册指令的方法
    public void RegisterCommand(string commandName, Func<CommandContext, Task> action)
    {
        _commands[commandName.ToLower()] = action; // 统一转小写，防止用户大小写混用
    }

    // 执行指令的方法
    public async Task ExecuteAsync(CommandContext context)
    {
        // 提取用户输入的第一部分作为指令名称（例如 "/epic 123" 提取出 "/epic"）
        string[] parts = context.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string commandName = parts[0].ToLower();

        // 检查字典里有没有注册这个指令
        if (_commands.TryGetValue(commandName, out var action))
        {
            // 如果有，把剩余的部分作为参数存入上下文
            context.Args = parts.Skip(1).ToArray();
            
            try
            {
                // 执行对应的指令逻辑
                await action.Invoke(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[指令执行报错] 指令: {commandName}, 错误: {ex.Message}");
            }
        }
    }
}