using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Milky.Net.Client;
using Milky.Net.Model;
using TL;

namespace MilkyQQBot.Services;

public static class TelegramMsgService
{
    private const string OffsetStatePath = "telegram_msg_offsets.json";

    // 相册缓冲时间：Telegram 的多图/多视频通常会在很短时间内连续到达
    // 这里等待 1500ms，把同一个 grouped_id 的消息收齐后再一次性转发
    private const int AlbumCollectDelayMs = 1500;

    private static readonly SemaphoreSlim _startLock = new(1, 1);
    private static readonly SemaphoreSlim _offsetLock = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    // 记录每个监听源已处理到的最后一条消息 ID，避免重复转发
    private static readonly ConcurrentDictionary<string, int> _lastMessageIds = LoadOffsets();

    // 相册缓冲区：key = sourceKey + groupedId
    private static readonly ConcurrentDictionary<string, BufferedAlbum> _albumBuffers =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _started;
    private static MilkyClient? _milky;
    private static TelegramConfig? _config;
    private static WTelegram.Client? _client;
    private static WTelegram.UpdateManager? _manager;

    private static Dictionary<string, WatchedPeer> _watchedPeers = new(StringComparer.OrdinalIgnoreCase);

    // 控制台输出锁：保证“提示用户输入”和“后台日志输出”不会互相抢占
    private static readonly object _consoleLock = new();

    // 当用户正在输入时，把后台日志先缓存起来，等输入结束后再统一刷出来
    private static readonly Queue<ConsoleLogEntry> _pendingConsoleLogs = new();

    // 当前是否处于“等待用户输入”的交互模式
    private static bool _consolePromptActive;

    // Telegram 媒体缓存定时清理任务
    private static CancellationTokenSource? _mediaCleanupCts;

    public static async Task StartAsync(MilkyClient milky, CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_started)
                return;

            try
            {
                _milky = milky;
                _config = AppConfig.Current.Telegram;

                if (_config == null || !_config.Enabled)
                {
                    LogInfo("Telegram 功能未开启。");
                    return;
                }

                if (_config.Sources == null || _config.Sources.Count == 0)
                {
                    LogInfo("未配置 Telegram 监听源，本次不启动 Telegram 转发。");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_config.ApiId))
                {
                    LogInfo("未填写 Telegram ApiId，本次不启动 Telegram 转发。");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_config.ApiHash))
                {
                    LogInfo("未填写 Telegram ApiHash，本次不启动 Telegram 转发。");
                    return;
                }

                string apiHash = _config.ApiHash.Trim();
                if (apiHash.Any(c => !Uri.IsHexDigit(c)))
                {
                    LogInfo("Telegram ApiHash 格式不正确，请检查配置文件。");
                    return;
                }

                // 如果本地已经有 session，先询问本次启动是否启用 Telegram 功能
                if (HasExistingSession())
                {
                    bool enableTelegram = AskEnableTelegramWithExistingSession();
                    if (!enableTelegram)
                    {
                        LogInfo("本次已跳过 Telegram 功能，QQ 机器人其它功能不受影响。");
                        return;
                    }
                }

                Directory.CreateDirectory(_config.MediaCacheDirectory);

                // 启动时先清理一遍历史过期媒体缓存
                int deletedCount = CleanupExpiredMediaFiles(
                    _config.MediaCacheDirectory,
                    TimeSpan.FromHours(Math.Max(1, _config.MediaKeepHours)));

                if (deletedCount > 0)
                {
                    LogInfo($"启动时已清理 {deletedCount} 个过期的 Telegram 媒体缓存文件。");
                }

                // 启动后台定时清理任务
                StartMediaCleanupLoop();

                // WTelegram 底层日志默认关闭，只保留少量关键中文提示
                WTelegram.Helpers.Log = CreateSdkLogHandler();

                _client = new WTelegram.Client(Config);
                _manager = _client.WithUpdateManager(Client_OnUpdate);

                // LoginUserIfNeeded 会自动处理：
                // - 已有 session 直接恢复
                // - 没有 session 时进入验证码/2FA/邮箱验证/注册信息流程
                var me = await _client.LoginUserIfNeeded();
                LogInfo($"Telegram 登录成功：{FormatUserName(me)}");

                var dialogs = await _client.Messages_GetAllDialogs();
                dialogs.CollectUsersChats(_manager.Users, _manager.Chats);

                _watchedPeers = await ResolveWatchedPeersAsync(_config.Sources);

                if (_watchedPeers.Count == 0)
                {
                    LogInfo("没有找到可用的 Telegram 监听目标，请检查频道链接和账号加入状态。");
                    SafeDisposeClient();
                    return;
                }

                await BootstrapAsync(cancellationToken);

                _started = true;

                LogInfo($"Telegram 服务已启动，共监听 {_watchedPeers.Count} 个目标。");
                foreach (var peer in _watchedPeers.Values)
                {
                    LogInfo($"已监听：{peer.DisplayName}");
                }
            }
            catch (OperationCanceledException)
            {
                // 用户在验证码/2FA/邮箱验证等步骤中输入 q
                _started = false;
                LogInfo("用户取消 Telegram 登录，Telegram 相关功能未开启，QQ 机器人其他功能不受影响。");
                SafeDisposeClient();
            }
            catch (Exception ex)
            {
                _started = false;
                LogError("启动失败，Telegram 服务不会影响主程序继续运行。", ex);
                SafeDisposeClient();
            }
        }
        finally
        {
            _startLock.Release();
        }
    }
    
    /// <summary>
    /// 只允许从完整的 Telegram 公共链接中提取用户名。
    /// 只接受这种格式：
    /// https://t.me/xxxx
    /// 
    /// 不再支持：
    /// - @xxxx
    /// - xxxx
    /// - 标题名
    /// - 数字 id
    /// - 其它域名/其它路径格式
    /// </summary>
    private static bool TryExtractUsernameFromTelegramUrl(string value, out string username)
    {
        username = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string raw = value.Trim();

        // 只允许 https://t.me/ 开头
        const string prefix = "https://t.me/";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string remaining = raw[prefix.Length..].Trim();

        // 去掉末尾 /
        remaining = remaining.Trim('/');

        // 只允许单段用户名，比如 zaihuapd
        // 不接受 /s/xxx、/joinchat/xxx、/c/xxx 这种路径
        if (string.IsNullOrWhiteSpace(remaining) || remaining.Contains('/'))
            return false;

        // 用户名里不应该再带 @
        if (remaining.StartsWith('@'))
            return false;

        username = remaining;
        return true;
    }

    private static string? Config(string what)
    {
        try
        {
            if (_config == null)
                return null;

            string? value = what switch
            {
                "api_id" => _config.ApiId?.Trim(),
                "api_hash" => _config.ApiHash?.Trim(),

                // 手机号如果配置里没填，就在控制台里补录；支持 q 取消
                "phone_number" => !string.IsNullOrWhiteSpace(_config.PhoneNumber)
                    ? _config.PhoneNumber.Trim()
                    : PromptRequiredInput("请输入 Telegram 手机号并回车(退出登录请输入q并回车): "),

                // 验证码：必须输入；输入 q 直接取消本次 Telegram 登录
                "verification_code" => PromptRequiredInput("请输入 Telegram 验证码并回车(退出登录请输入q并回车): "),

                // 两步验证密码：如果配置里已有就直接用，否则提示输入；支持 q 取消
                "password" => !string.IsNullOrWhiteSpace(_config.Password)
                    ? _config.Password.Trim()
                    : PromptRequiredInput("请输入 Telegram 两步验证密码并回车(退出登录请输入q并回车): "),

                // 某些账号场景会要求提供邮箱
                "email" => PromptRequiredInput("请输入 Telegram 邮箱地址并回车(退出登录请输入q并回车): "),

                // 邮箱验证码
                "email_verification_code" => PromptRequiredInput("请输入 Telegram 邮箱验证码并回车(退出登录请输入q并回车): "),

                // Telegram 新账号注册时可能需要 first_name / last_name
                "first_name" => PromptRequiredInput("检测到需要注册 Telegram 账号，请输入 first_name 并回车(退出登录请输入q并回车): "),
                "last_name" => PromptRequiredInput("请输入 last_name 并回车(退出登录请输入q并回车): "),

                // session 文件路径
                "session_pathname" => GetSessionPath(),

                _ => null
            };

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"读取 WTelegram 配置失败: {what}", ex);
            return null;
        }
    }

    /// <summary>
    /// 当前配置对应的 session 文件路径。
    /// 如果配置为空，则默认使用 WTelegram.session。
    /// </summary>
    private static string GetSessionPath()
    {
        return string.IsNullOrWhiteSpace(_config?.SessionPath)
            ? "WTelegram.session"
            : _config!.SessionPath.Trim();
    }

    /// <summary>
    /// 判断本地是否已经存在可复用的 WTelegram.session。
    /// </summary>
    private static bool HasExistingSession()
    {
        string sessionPath = GetSessionPath();
        return File.Exists(sessionPath);
    }

    /// <summary>
    /// 当本地已有 session 时，询问用户本次是否启用 Telegram 功能。
    /// 输入 1 = 启用，0 = 不启用，直接回车默认 1。
    /// </summary>
    private static bool AskEnableTelegramWithExistingSession()
    {
        string answer = PromptWithPausedLogs(
            "检测到已存在 WTelegram.session，是否开启 Telegram 相关功能？开启请输入1，关闭请输入0，直接回车默认1: ",
            defaultValue: "1");

        return answer != "0";
    }

    /// <summary>
    /// 读取一个“必填项”输入。
    /// - 输入 q：取消 Telegram 登录
    /// - 输入空：继续提示，直到用户输入有效值
    /// </summary>
    private static string PromptRequiredInput(string prompt)
    {
        while (true)
        {
            string answer = PromptWithPausedLogs(prompt);

            if (string.Equals(answer, "q", StringComparison.OrdinalIgnoreCase))
                throw new OperationCanceledException("用户取消 Telegram 登录。");

            if (!string.IsNullOrWhiteSpace(answer))
                return answer.Trim();

            WriteConsoleLine("[TelegramMsg] 输入不能为空，请重新输入。", isError: true);
        }
    }

    /// <summary>
    /// 在等待用户输入时暂停其它日志输出。
    /// 输入结束后，再把期间缓存的日志统一刷出来。
    /// </summary>
    private static string PromptWithPausedLogs(string prompt, string defaultValue = "")
    {
        lock (_consoleLock)
        {
            _consolePromptActive = true;
            Console.Write(prompt);
        }

        string? input = Console.ReadLine();

        lock (_consoleLock)
        {
            _consolePromptActive = false;
            FlushPendingConsoleLogs_NoLock();
        }

        input = input?.Trim();

        if (string.IsNullOrWhiteSpace(input))
            return defaultValue;

        return input;
    }

    /// <summary>
    /// 首次启动时建立每个监听源的基线消息位置，避免补发历史消息。
    /// </summary>
    private static async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        if (_client == null || _config == null)
            return;

        bool changed = false;

        foreach (var peer in _watchedPeers.Values)
        {
            if (_lastMessageIds.ContainsKey(peer.SourceKey))
                continue;

            if (!_config.BootstrapWithoutPush)
            {
                _lastMessageIds[peer.SourceKey] = 0;
                changed = true;
                continue;
            }

            Messages_MessagesBase history = peer.User != null
                ? await _client.Messages_GetHistory(peer.User, limit: 1)
                : await _client.Messages_GetHistory(peer.Chat!, limit: 1);

            int lastId = history.Messages
                .OfType<Message>()
                .Select(x => x.ID)
                .DefaultIfEmpty(0)
                .Max();

            _lastMessageIds[peer.SourceKey] = lastId;
            changed = true;

            LogInfo($"已记录“{peer.DisplayName}”的当前位置，旧消息不会补发。");
        }

        if (changed)
        {
            await SaveOffsetsAsync(cancellationToken);
        }
    }

    private static async Task Client_OnUpdate(Update update)
    {
        try
        {
            switch (update)
            {
                case UpdateNewChannelMessage uncm:
                    await HandleMessageAsync(uncm.message);
                    break;

                case UpdateNewMessage unm:
                    await HandleMessageAsync(unm.message);
                    break;

                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            LogError($"处理 Telegram Update 失败: {update.GetType().Name}", ex);
        }
    }

    /// <summary>
    /// 统一处理单条 Telegram 更新。
    /// 如果是相册/媒体组消息，则先缓冲，等收齐后再一次性转发。
    /// </summary>
    private static async Task HandleMessageAsync(MessageBase messageBase)
    {
        try
        {
            if (!_started || _client == null || _milky == null)
                return;

            if (messageBase is not Message message)
                return;

            string? sourceKey = GetPeerKey(message.peer_id);
            if (string.IsNullOrWhiteSpace(sourceKey))
                return;

            if (!_watchedPeers.TryGetValue(sourceKey, out var watchedPeer))
                return;

            // 相册/媒体组：Telegram 会拆成多条消息推送过来，但 grouped_id 一样
            // 这里不立刻发，而是缓冲后合并发送
            if (message.grouped_id != 0)
            {
                BufferGroupedMessage(sourceKey, watchedPeer, message);
                return;
            }

            // 普通单条消息：直接处理
            await ProcessSingleMessageAsync(sourceKey, watchedPeer, message);
        }
        catch (Exception ex)
        {
            LogError("处理 Telegram 消息失败。", ex);
        }
    }

    /// <summary>
    /// 处理普通单条消息。
    /// </summary>
    private static async Task ProcessSingleMessageAsync(string sourceKey, WatchedPeer watchedPeer, Message message)
    {
        TelegramDispatchPayload? payload = await BuildPayloadFromSingleMessageAsync(watchedPeer, message);

        // 无论是否转发，都推进 offset，避免反复处理
        await SetLastMessageIdAsync(sourceKey, message.ID);

        // 包含普通文件的消息会被忽略
        if (payload == null)
            return;

        await DispatchToGroupsAsync(watchedPeer, payload);
    }

    /// <summary>
    /// 把同一个 grouped_id 的 Telegram 相册/媒体组消息缓冲起来。
    /// 每次有新消息到来都重新计时，直到短时间内不再有新消息进入，才统一转发。
    /// </summary>
    private static void BufferGroupedMessage(string sourceKey, WatchedPeer watchedPeer, Message message)
    {
        string albumKey = $"{sourceKey}:album:{message.grouped_id}";

        var buffer = _albumBuffers.GetOrAdd(albumKey, _ => new BufferedAlbum
        {
            SourceKey = sourceKey,
            WatchedPeer = watchedPeer,
            GroupedId = message.grouped_id
        });

        CancellationTokenSource? oldDelayCts = null;
        CancellationToken newToken;

        lock (buffer.SyncRoot)
        {
            // 避免同一条消息重复入缓冲区
            if (buffer.Messages.All(x => x.ID != message.ID))
            {
                buffer.Messages.Add(message);
            }

            oldDelayCts = buffer.DelayCts;
            buffer.DelayCts = new CancellationTokenSource();
            newToken = buffer.DelayCts.Token;
        }

        try
        {
            oldDelayCts?.Cancel();
        }
        catch
        {
            // 忽略取消异常
        }
        finally
        {
            oldDelayCts?.Dispose();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AlbumCollectDelayMs, newToken);
                await FlushBufferedAlbumAsync(albumKey);
            }
            catch (OperationCanceledException)
            {
                // 说明又有同 grouped_id 的消息来了，重新计时即可
            }
            catch (Exception ex)
            {
                LogError("处理 Telegram 图片/视频组时发生异常。", ex);
            }
        });
    }

    /// <summary>
    /// 把缓冲好的相册/媒体组一次性合并并转发。
    /// </summary>
    private static async Task FlushBufferedAlbumAsync(string albumKey)
    {
        if (!_albumBuffers.TryRemove(albumKey, out var buffer))
            return;

        List<Message> messages;
        CancellationTokenSource? delayCts;

        lock (buffer.SyncRoot)
        {
            messages = buffer.Messages
                .OrderBy(x => x.ID)
                .ToList();

            delayCts = buffer.DelayCts;
            buffer.DelayCts = null;
        }

        delayCts?.Dispose();

        if (messages.Count == 0)
            return;

        TelegramDispatchPayload? payload = await BuildPayloadFromAlbumAsync(buffer.WatchedPeer, messages);

        int maxMessageId = messages.Max(x => x.ID);
        await SetLastMessageIdAsync(buffer.SourceKey, maxMessageId);

        // 包含普通文件的相册/媒体组一律忽略
        if (payload == null)
            return;

        await DispatchToGroupsAsync(buffer.WatchedPeer, payload);
    }

    /// <summary>
    /// 把构造好的 QQ 转发内容分发到所有启用 TelegramMsg 的群。
    /// </summary>
    private static async Task DispatchToGroupsAsync(WatchedPeer watchedPeer, TelegramDispatchPayload payload)
    {
        var groupIds = GroupConfigManager.GetTelegramMsgEnabledGroupIds();
        int successCount = 0;

        foreach (long groupId in groupIds)
        {
            try
            {
                await SendToGroupAsync(groupId, payload);
                successCount++;
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                LogError($"向群 {groupId} 转发消息失败。", ex);
            }
        }

        if (_config?.LogForwardSuccess == true && successCount > 0)
        {
            LogInfo($"“{watchedPeer.DisplayName}”的新消息已转发到 {successCount} 个 QQ 群。");
        }
    }

    /// <summary>
    /// 构造普通单条 Telegram 消息的转发内容。
    /// 规则：
    /// - 文字 -> From xxx + \n + 文字
    /// - 图片 -> From xxx + \n + 图片
    /// - 文字+图片 -> From xxx + \n + 文字 + \n + 图片
    /// - video -> 头部消息 + 每个视频单独一条
    /// - 文件 -> 忽略
    /// </summary>
    private static async Task<TelegramDispatchPayload?> BuildPayloadFromSingleMessageAsync(WatchedPeer watchedPeer, Message message)
    {
        var payload = new TelegramDispatchPayload
        {
            SourceName = watchedPeer.DisplayName,
            Text = TelegramTextCleaner.BuildText(message)
        };

        switch (message.media)
        {
            case null:
                break;

            case MessageMediaPhoto { photo: Photo photo }:
            {
                string? imagePath = await DownloadPhotoAsync(photo);
                if (!string.IsNullOrWhiteSpace(imagePath))
                    payload.ImageUris.Add(ToFileUri(imagePath));
                break;
            }

            case MessageMediaDocument { document: Document document }:
            {
                bool accepted = await FillDocumentPayloadAsync(document, payload);
                if (!accepted)
                    return null; // 普通文件一律忽略
                break;
            }

            default:
                // 其它媒体类型（如投票、地理位置等）不处理
                break;
        }

        if (string.IsNullOrWhiteSpace(payload.Text)
            && payload.ImageUris.Count == 0
            && payload.VideoUris.Count == 0)
        {
            return null;
        }

        return payload;
    }

    /// <summary>
    /// 构造 Telegram 相册/媒体组的转发内容。
    /// 规则：
    /// - n 张图片 -> 一条 QQ 消息统一发送
    /// - 文字+n张图片 -> 一条 QQ 消息统一发送
    /// - n 段视频 -> 头部消息 + 每段视频单独一条
    /// - 文字+n段视频 -> 头部/文字一条 + 每段视频单独一条
    /// - 含普通文件 -> 整组忽略
    /// </summary>
    private static async Task<TelegramDispatchPayload?> BuildPayloadFromAlbumAsync(
        WatchedPeer watchedPeer,
        IReadOnlyList<Message> messages)
    {
        var payload = new TelegramDispatchPayload
        {
            SourceName = watchedPeer.DisplayName
        };

        foreach (Message message in messages.OrderBy(x => x.ID))
        {
            // 相册 caption 一般只出现在其中一条，取第一段非空文本即可
            if (string.IsNullOrWhiteSpace(payload.Text))
            {
                string text = TelegramTextCleaner.BuildText(message);
                if (!string.IsNullOrWhiteSpace(text))
                    payload.Text = text;
            }

            switch (message.media)
            {
                case MessageMediaPhoto { photo: Photo photo }:
                {
                    string? imagePath = await DownloadPhotoAsync(photo);
                    if (!string.IsNullOrWhiteSpace(imagePath))
                        payload.ImageUris.Add(ToFileUri(imagePath));
                    break;
                }

                case MessageMediaDocument { document: Document document }:
                {
                    bool accepted = await FillDocumentPayloadAsync(document, payload);
                    if (!accepted)
                        return null; // 整组中出现普通文件，则整组忽略
                    break;
                }

                case null:
                    break;

                default:
                    break;
            }
        }

        payload.ImageUris = payload.ImageUris
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        payload.VideoUris = payload.VideoUris
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(payload.Text)
            && payload.ImageUris.Count == 0
            && payload.VideoUris.Count == 0)
        {
            return null;
        }

        return payload;
    }

    /// <summary>
    /// 处理 Telegram document。
    /// 返回 true 表示可转发（图片/视频），false 表示普通文件，需要整条忽略。
    /// </summary>
    private static async Task<bool> FillDocumentPayloadAsync(Document document, TelegramDispatchPayload payload)
    {
        string mimeType = document.mime_type ?? string.Empty;

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            string path = await DownloadDocumentAsync(document, ".jpg");
            payload.ImageUris.Add(ToFileUri(path));
            return true;
        }

        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            string path = await DownloadDocumentAsync(document, ".mp4");
            payload.VideoUris.Add(ToFileUri(path));
            return true;
        }

        // 按规则：普通文件消息一律忽略，不转发
        return false;
    }

    /// <summary>
    /// 下载 Telegram 照片到本地临时目录。
    /// </summary>
    private static async Task<string?> DownloadPhotoAsync(Photo photo)
    {
        if (_client == null || _config == null)
            return null;

        string path = Path.Combine(_config.MediaCacheDirectory, $"photo_{photo.id}.jpg");

        await using var stream = File.Create(path);
        await _client.DownloadFileAsync(photo, stream);

        return path;
    }

    /// <summary>
    /// 下载 Telegram document 到本地临时目录。
    /// 为避免同名覆盖，文件名中会追加 document.id。
    /// </summary>
    private static async Task<string> DownloadDocumentAsync(Document document, string fallbackExtension)
    {
        if (_client == null || _config == null)
            throw new InvalidOperationException("Telegram 客户端未初始化。");

        string originalName = !string.IsNullOrWhiteSpace(document.Filename)
            ? document.Filename
            : $"document_{document.id}{GuessExtension(document.mime_type, fallbackExtension)}";

        originalName = SanitizeFileName(originalName);

        string extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = GuessExtension(document.mime_type, fallbackExtension);
        }

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalName);
        string finalFileName = $"{fileNameWithoutExtension}_{document.id}{extension}";

        string path = Path.Combine(_config.MediaCacheDirectory, finalFileName);

        await using var stream = File.Create(path);
        await _client.DownloadFileAsync(document, stream);

        return path;
    }

    /// <summary>
    /// 按规则把一条 Telegram 消息发送到指定 QQ 群。
    /// 规则总结：
    /// 1. 第一行永远是 From xxx
    /// 2. 图片可和文字同条发送
    /// 3. 视频因为 QQ 限制，只能一个视频一条消息
    /// 4. 文件不转发（已在构造 payload 时过滤）
    /// </summary>
    private static async Task SendToGroupAsync(long groupId, TelegramDispatchPayload payload)
    {
        if (_milky == null)
            return;

        var ctx = CommandContext.CreateGroup(_milky, groupId);

        string header = BuildHeader(payload.SourceName);
        string headerWithText = BuildHeaderWithOptionalText(payload.SourceName, payload.Text);

        bool hasText = !string.IsNullOrWhiteSpace(payload.Text);
        bool hasImages = payload.ImageUris.Count > 0;
        bool hasVideos = payload.VideoUris.Count > 0;

        // 1) 纯文字 -> 一条消息：From xxx + \n + 文字
        if (hasText && !hasImages && !hasVideos)
        {
            await ctx.TextAsync(headerWithText);
            return;
        }

        // 2) 图片 / 多图 / 文字+多图 -> 一条消息统一发送
        //    第一行固定 From xxx，后面带文字（如果有），最后跟所有图片
        if (hasImages && !hasVideos)
        {
            var segments = new List<OutgoingSegment>
            {
                CommandContext.Seg.Text(hasText ? headerWithText : header)
            };

            foreach (string imageUri in payload.ImageUris.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                segments.Add(CommandContext.Seg.Image(imageUri));
            }

            await ctx.SendAsync(segments.ToArray());
            return;
        }

        // 3) video / 多个 video / 文字+多个 video
        //    第一条只发头部（或头部+文字），后面的每条只发一个 video
        if (hasVideos)
        {
            // 如果还有图片，就把图片和头部/文字放在第一条一起发
            if (hasImages)
            {
                var firstSegments = new List<OutgoingSegment>
                {
                    CommandContext.Seg.Text(hasText ? headerWithText : header)
                };

                foreach (string imageUri in payload.ImageUris.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    firstSegments.Add(CommandContext.Seg.Image(imageUri));
                }

                await ctx.SendAsync(firstSegments.ToArray());
                await Task.Delay(800);
            }
            else
            {
                await ctx.TextAsync(hasText ? headerWithText : header);
                await Task.Delay(500);
            }

            foreach (string videoUri in payload.VideoUris.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await ctx.VideoAsync(videoUri);
                }
                catch (Exception ex)
                {
                    LogError($"向群 {groupId} 发送视频失败。", ex);
                }

                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// 头部固定为：From xxx
    /// xxx 取 appsettings.json 中 Telegram->Sources->Alias
    /// 如果没配 Alias，则退回到频道标题。
    /// </summary>
    private static string BuildHeader(string sourceName)
    {
        return $"<From {sourceName}>";
    }

    /// <summary>
    /// 头部 + 正文。
    /// 正文为空时只返回头部。
    /// </summary>
    private static string BuildHeaderWithOptionalText(string sourceName, string? text)
    {
        string header = BuildHeader(sourceName);

        if (string.IsNullOrWhiteSpace(text))
            return header;

        return $"{header}\n{text}";
    }
    

    /// <summary>
    /// 解析发送者名称。
    /// 目前频道转发规则不显示发送者，但保留方法，后续扩展群聊/私聊时可用。
    /// </summary>
    private static string? ResolveSenderName(Message message)
    {
        if (_manager == null)
            return null;

        return message.from_id switch
        {
            PeerUser peerUser when _manager.Users.TryGetValue(peerUser.user_id, out var user)
                => FormatUserName(user),

            PeerChannel peerChannel when _manager.Chats.TryGetValue(peerChannel.channel_id, out var channel)
                => channel.Title,

            PeerChat peerChat when _manager.Chats.TryGetValue(peerChat.chat_id, out var chat)
                => chat.Title,

            _ => null
        };
    }

    private static string FormatUserName(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.username))
            return "@" + user.username;

        string fullName = $"{user.first_name} {user.last_name}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? $"User({user.id})" : fullName;
    }

    /// <summary>
    /// 解析配置文件里的 Telegram 监听源。
    /// 
    /// 现在只支持这种配置：
    /// Value = https://t.me/xxx
    /// 
    /// 不再支持 @username、标题名、数字 id。
    /// </summary>
    private static async Task<Dictionary<string, WatchedPeer>> ResolveWatchedPeersAsync(
        IEnumerable<TelegramSourceConfig> sources)
    {
        var result = new Dictionary<string, WatchedPeer>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            string type = (source.Type ?? "channel").Trim().ToLowerInvariant();

            WatchedPeer? peer = type switch
            {
                "channel" => await ResolveChannelByUrlAsync(source.Value, source.Alias),
                "group" or "chat" => await ResolveGroupByUrlAsync(source.Value, source.Alias),
                "user" or "private" => await ResolveUserByUrlAsync(source.Value, source.Alias),
                _ => null
            };

            if (peer == null)
            {
                LogInfo($"监听目标无效：{source.Value}");
                continue;
            }

            result[peer.SourceKey] = peer;
        }

        return result;
    }
    /// <summary>
    /// 通过完整的 Telegram 公开链接解析频道。
    /// 只支持：https://t.me/xxx
    /// </summary>
    private static async Task<WatchedPeer?> ResolveChannelByUrlAsync(string value, string? alias)
    {
        if (_client == null)
            return null;

        if (!TryExtractUsernameFromTelegramUrl(value, out string username))
        {
            LogInfo($"频道链接格式不正确：{value}");
            return null;
        }

        try
        {
            var resolved = await _client.Contacts_ResolveUsername(username);

            if (resolved.Chat is Channel channel && channel.IsActive && channel.IsChannel)
            {
                return new WatchedPeer
                {
                    SourceKey = $"channel:{channel.id}",
                    DisplayName = string.IsNullOrWhiteSpace(alias) ? channel.Title : alias,
                    Chat = channel
                };
            }

            LogInfo($"该链接不是频道：{value}");
            return null;
        }
        catch (TL.RpcException ex)
        {
            LogInfo($"无法解析频道：{value}");
            return null;
        }
        catch (Exception ex)
        {
            LogError($"解析频道链接时发生异常：{value}", ex);
            return null;
        }
    }
    
    /// <summary>
    /// 通过完整的 Telegram 公开链接解析群组。
    /// 只支持：https://t.me/xxx
    /// </summary>
    private static async Task<WatchedPeer?> ResolveGroupByUrlAsync(string value, string? alias)
    {
        if (_client == null)
            return null;

        if (!TryExtractUsernameFromTelegramUrl(value, out string username))
        {
            LogInfo($"群组链接格式不正确：{value}");
            return null;
        }

        try
        {
            var resolved = await _client.Contacts_ResolveUsername(username);

            switch (resolved.Chat)
            {
                case Channel channel when channel.IsActive && !channel.IsChannel:
                    return new WatchedPeer
                    {
                        SourceKey = $"channel:{channel.id}",
                        DisplayName = string.IsNullOrWhiteSpace(alias) ? channel.Title : alias,
                        Chat = channel
                    };

                case Chat chat when chat.IsActive:
                    return new WatchedPeer
                    {
                        SourceKey = $"chat:{chat.id}",
                        DisplayName = string.IsNullOrWhiteSpace(alias) ? chat.Title : alias,
                        Chat = chat
                    };

                default:
                    LogInfo($"该链接不是群组：{value}");
                    return null;
            }
        }
        catch (TL.RpcException ex)
        {
            LogInfo($"无法解析群组：{value}");
            return null;
        }
        catch (Exception ex)
        {
            LogError($"解析群组链接时发生异常：{value}", ex);
            return null;
        }
    }
    
    /// <summary>
    /// 通过完整的 Telegram 公开链接解析用户。
    /// 只支持：https://t.me/xxx
    /// </summary>
    private static async Task<WatchedPeer?> ResolveUserByUrlAsync(string value, string? alias)
    {
        if (_client == null)
            return null;

        if (!TryExtractUsernameFromTelegramUrl(value, out string username))
        {
            LogInfo($"用户链接格式不正确：{value}");
            return null;
        }

        try
        {
            var resolved = await _client.Contacts_ResolveUsername(username);

            if (resolved.User is User user)
            {
                return new WatchedPeer
                {
                    SourceKey = $"user:{user.id}",
                    DisplayName = string.IsNullOrWhiteSpace(alias) ? FormatUserName(user) : alias,
                    User = user
                };
            }

            LogInfo($"该链接不是用户：{value}");
            return null;
        }
        catch (TL.RpcException ex)
        {
            LogInfo($"无法解析用户：{value}");
            return null;
        }
        catch (Exception ex)
        {
            LogError($"解析用户链接时发生异常：{value}", ex);
            return null;
        }
    }

    private static string? GetPeerKey(Peer? peer)
    {
        return peer switch
        {
            PeerChannel channel => $"channel:{channel.channel_id}",
            PeerChat chat => $"chat:{chat.chat_id}",
            PeerUser user => $"user:{user.user_id}",
            _ => null
        };
    }

    private static int GetLastMessageId(string sourceKey)
    {
        return _lastMessageIds.TryGetValue(sourceKey, out int value) ? value : 0;
    }

    private static async Task SetLastMessageIdAsync(string sourceKey, int messageId)
    {
        _lastMessageIds.AddOrUpdate(sourceKey, messageId, (_, oldValue) => Math.Max(oldValue, messageId));
        await SaveOffsetsAsync();
    }

    private static ConcurrentDictionary<string, int> LoadOffsets()
    {
        try
        {
            if (!File.Exists(OffsetStatePath))
                return new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            string json = File.ReadAllText(OffsetStatePath);
            return JsonSerializer.Deserialize<ConcurrentDictionary<string, int>>(json)
                   ?? new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task SaveOffsetsAsync(CancellationToken cancellationToken = default)
    {
        await _offsetLock.WaitAsync(cancellationToken);
        try
        {
            string json = JsonSerializer.Serialize(_lastMessageIds, _jsonOptions);
            await File.WriteAllTextAsync(OffsetStatePath, json, cancellationToken);
        }
        finally
        {
            _offsetLock.Release();
        }
    }

    private static string GuessExtension(string? mimeType, string fallbackExtension)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return fallbackExtension;

        return mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            _ => fallbackExtension
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }

        return fileName;
    }

    private static string ToFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    /// <summary>
    /// 清理过期的 Telegram 媒体缓存文件。
    /// 返回本次实际删除的文件数量。
    /// </summary>
    private static int CleanupExpiredMediaFiles(string directory, TimeSpan keepTime)
    {
        if (!Directory.Exists(directory))
            return 0;

        int deletedCount = 0;
        DateTime threshold = DateTime.UtcNow - keepTime;

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                    deletedCount++;
                }
            }
            catch
            {
                // 忽略单个文件删除失败，不影响后续文件继续清理
            }
        }

        return deletedCount;
    }

    private sealed class WatchedPeer
    {
        public string SourceKey { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public ChatBase? Chat { get; init; }
        public User? User { get; init; }
    }

    private sealed class TelegramDispatchPayload
    {
        public string SourceName { get; init; } = "";
        public string Text { get; set; } = "";
        public List<string> ImageUris { get; set; } = new();
        public List<string> VideoUris { get; set; } = new();
    }

    /// <summary>
    /// Telegram 相册/媒体组缓冲对象。
    /// </summary>
    private sealed class BufferedAlbum
    {
        public string SourceKey { get; init; } = "";
        public long GroupedId { get; init; }
        public WatchedPeer WatchedPeer { get; init; } = null!;
        public List<Message> Messages { get; } = new();
        public object SyncRoot { get; } = new();
        public CancellationTokenSource? DelayCts { get; set; }
    }

    private sealed class ConsoleLogEntry
    {
        public string Text { get; init; } = "";
        public bool IsError { get; init; }
    }

    /// <summary>
    /// 统一的控制台输出入口。
    /// 如果当前正处于用户输入阶段，则先缓存日志，等输入结束后再统一输出。
    /// </summary>
    private static void WriteConsoleLine(string text, bool isError = false)
    {
        lock (_consoleLock)
        {
            if (_consolePromptActive)
            {
                _pendingConsoleLogs.Enqueue(new ConsoleLogEntry
                {
                    Text = text,
                    IsError = isError
                });
                return;
            }

            WriteConsoleLine_NoLock(text, isError);
        }
    }

    /// <summary>
    /// 仅在已经持有 _consoleLock 时调用。
    /// </summary>
    private static void WriteConsoleLine_NoLock(string text, bool isError)
    {
        var oldColor = Console.ForegroundColor;

        if (isError)
            Console.ForegroundColor = ConsoleColor.Red;

        Console.WriteLine(text);
        Console.ForegroundColor = oldColor;
    }

    /// <summary>
    /// 仅在已经持有 _consoleLock 时调用。
    /// 把输入阶段缓存的日志按顺序刷出来。
    /// </summary>
    private static void FlushPendingConsoleLogs_NoLock()
    {
        while (_pendingConsoleLogs.Count > 0)
        {
            var entry = _pendingConsoleLogs.Dequeue();
            WriteConsoleLine_NoLock(entry.Text, entry.IsError);
        }
    }
    
    /// <summary>
    /// 输出普通日志。
    /// 日志前缀统一使用 [TG]，尽量只保留关键流程信息。
    /// </summary>
    private static void LogInfo(string message)
    {
        WriteConsoleLine($"[TG] {message}");
    }

    /// <summary>
    /// 输出异常日志。
    /// 第一行使用简短中文说明，第二行再附异常详情，方便排查。
    /// </summary>
    private static void LogError(string scene, Exception ex)
    {
        string text = $"[TG] {scene}{Environment.NewLine}{ex}";
        WriteConsoleLine(text, isError: true);
    }

    private static void SafeDisposeClient()
    {
        try
        {
            StopMediaCleanupLoop();

            // 清掉还没来得及发送的相册缓冲
            foreach (var pair in _albumBuffers)
            {
                try
                {
                    pair.Value.DelayCts?.Cancel();
                    pair.Value.DelayCts?.Dispose();
                }
                catch
                {
                    // 忽略释放异常
                }
            }
            _albumBuffers.Clear();

            _client?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
        finally
        {
            _client = null;
            _manager = null;
        }
    }

    /// <summary>
    /// 启动 Telegram 媒体缓存的后台定时清理任务。
    /// </summary>
    private static void StartMediaCleanupLoop()
    {
        StopMediaCleanupLoop();

        if (_config == null)
            return;

        _mediaCleanupCts = new CancellationTokenSource();
        _ = Task.Run(() => MediaCleanupLoopAsync(_mediaCleanupCts.Token));
    }

    /// <summary>
    /// 停止 Telegram 媒体缓存的后台定时清理任务。
    /// </summary>
    private static void StopMediaCleanupLoop()
    {
        try
        {
            _mediaCleanupCts?.Cancel();
        }
        catch
        {
            // 忽略取消异常
        }
        finally
        {
            _mediaCleanupCts?.Dispose();
            _mediaCleanupCts = null;
        }
    }

    /// <summary>
    /// 后台循环清理 telegram_media 中的过期文件。
    /// 默认每隔 MediaCleanupIntervalMinutes 分钟清理一次。
    /// </summary>
    private static async Task MediaCleanupLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_config == null)
                    return;

                int intervalMinutes = Math.Max(10, _config.MediaCleanupIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cancellationToken);

                int deletedCount = CleanupExpiredMediaFiles(
                    _config.MediaCacheDirectory,
                    TimeSpan.FromHours(Math.Max(1, _config.MediaKeepHours)));

                if (deletedCount > 0)
                {
                    LogInfo($"已自动清理 {deletedCount} 个过期的 Telegram 媒体缓存文件。");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogError("执行 Telegram 媒体缓存定时清理时发生异常。", ex);
            }
        }
    }

    /// <summary>
    /// 创建 WTelegram SDK 日志处理器。
    /// 默认只输出少量关键中文提示；如果开启 VerboseSdkLog，才输出原始底层日志。
    /// </summary>
    private static Action<int, string> CreateSdkLogHandler()
    {
        return (_, message) =>
        {
            if (_config?.VerboseSdkLog == true)
            {
                LogInfo($"[Telegram 底层] {message}");
                return;
            }

            if (TryTranslateSdkLog(message, out string readableMessage))
            {
                LogInfo(readableMessage);
            }
        };
    }

    /// <summary>
    /// 把少量关键的 WTelegram 原始日志翻译成更容易理解的中文提示。
    /// 其余大多数底层日志直接忽略。
    /// </summary>
    private static bool TryTranslateSdkLog(string rawMessage, out string readableMessage)
    {
        readableMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawMessage))
            return false;

        if (rawMessage.Contains("A verification code has been sent", StringComparison.OrdinalIgnoreCase))
        {
            readableMessage = "验证码已发送，请查看 Telegram 后输入。";
            return true;
        }

        if (rawMessage.Contains("Connected to DC", StringComparison.OrdinalIgnoreCase))
        {
            readableMessage = "已连接到 Telegram 服务器。";
            return true;
        }

        if (rawMessage.Contains("PHONE_MIGRATE_", StringComparison.OrdinalIgnoreCase))
        {
            readableMessage = "正在切换到正确的 Telegram 服务器。";
            return true;
        }

        if (rawMessage.Contains("Disposing the client", StringComparison.OrdinalIgnoreCase))
        {
            readableMessage = "Telegram 客户端已关闭。";
            return true;
        }

        return false;
    }
}