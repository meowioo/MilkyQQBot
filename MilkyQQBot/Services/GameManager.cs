using System;
using System.Drawing;
using System.Collections.Concurrent;

namespace MilkyQQBot.Services
{
    // 玩家游戏数据实体
    public class PlayerGameData
    {
        public long UserId { get; set; }
        public int HP { get; set; } = 100;
        public int MaxHP { get; set; } = 100;
        public int ATK { get; set; } = 10;
        public int DEF { get; set; } = 5;
        public int Gold { get; set; } = 0;
        public int StepIndex { get; set; } = 0; // 对应坐标数组的索引 (0代表第1格)
        public bool IsMovingForward { get; set; } = true; // 记录移动方向：true为向终点进发，false为向起点折返
    }

    // 全局游戏管理器
    public static class GameManager
    {
        // 记录哪些群开启了游戏 <GroupId, IsOpen>
        public static ConcurrentDictionary<long, bool> GroupGameStates = new();
        
        // 内存中的玩家数据 <UserId, PlayerGameData> 
        // (后续建议将此部分持久化到你的 DatabaseManager.cs 中)
        public static ConcurrentDictionary<long, PlayerGameData> Players = new();

        // 修正后的地图坐标数组 (共 89 格，索引 0-88)
        public static readonly Point[] MapRoute = new Point[]
        {
            new Point(32, 96),   new Point(64, 96),   new Point(96, 96),   new Point(128, 96),  new Point(160, 96),
            new Point(192, 96),  new Point(224, 96),  new Point(256, 96),  new Point(288, 96),  new Point(288, 128),
            new Point(320, 128), new Point(352, 160), new Point(384, 160), new Point(416, 160), new Point(448, 160),
            new Point(480, 160), new Point(480, 128), new Point(480, 96),  new Point(512, 96),  new Point(544, 96),
            new Point(560, 112), new Point(560, 144), new Point(560, 176), new Point(560, 208), new Point(560, 240),
            new Point(576, 256), new Point(576, 288), new Point(560, 304), new Point(560, 336), new Point(528, 336),
            new Point(496, 336), new Point(464, 300), new Point(464, 272), new Point(433, 273), new Point(416, 255),
            new Point(400, 240), new Point(370, 240), new Point(336, 240), new Point(320, 256), new Point(288, 256),
            new Point(272, 272), new Point(256, 304), new Point(225, 304), new Point(209, 286), new Point(176, 272),
            new Point(144, 272), new Point(128, 288), new Point(128, 320), new Point(112, 336), new Point(80, 336),
            new Point(64, 352),  new Point(64, 382),  new Point(64, 416),  new Point(80, 432),  new Point(113, 432),
            new Point(145, 432), new Point(177, 432), new Point(208, 432), new Point(240, 432), new Point(257, 415),
            new Point(288, 400), new Point(320, 400), new Point(352, 416), new Point(352, 448), new Point(383, 448),
            new Point(416, 463), new Point(450, 463), new Point(480, 463), new Point(512, 463), new Point(544, 480),
            new Point(544, 512), new Point(512, 544), new Point(496, 560), new Point(464, 575), new Point(433, 575),
            new Point(400, 575), new Point(369, 560), new Point(353, 544), new Point(320, 528), new Point(287, 528),
            new Point(257, 528), new Point(240, 560), new Point(207, 560), new Point(176, 560), new Point(144, 560),
            new Point(112, 560), new Point(80, 560),  new Point(64, 592),  new Point(64, 623)
        };
    }
}