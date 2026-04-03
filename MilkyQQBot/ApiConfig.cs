namespace MilkyQQBot;

public static class ApiConfig
{
    // ==========================================
    // 基础连接配置
    // ==========================================
    // WebSocket 连接地址
    //public const string WebSocketUrl = "ws://localhost:3010/";
    
    // 如果以后需要用到 AccessToken，也可以统一放在这里
    // public const string AccessToken = "Bearer hhxx8910";

    // ==========================================
    // 外部 API 接口地址管理
    // ==========================================
    public static class ApiUrls
    {
        // Epic 本周限免游戏海报 API
        public const string EpicFreeGames = "https://api.lolimi.cn/API/epic/epic?type=img";

        // 今日摸鱼日历 API
        public const string MoyuCalendar = "https://api.lolimi.cn/API/60s/moyu_calendar";

        // 早晚安问候图片 API
        public const string MorningNightImage = "https://api.lolimi.cn/API/image-zw/api";
    }
}