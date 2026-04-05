namespace MilkyQQBot.Models
{
    public class PlayerGameData
    {
        public long UserId { get; set; }
        public int HP { get; set; } = 100;
        public int MaxHP { get; set; } = 100;
        public int ATK { get; set; } = 10;
        public int DEF { get; set; } = 5;
        public int Gold { get; set; } = 0;
        public int StepIndex { get; set; } = 0; 
        
        // 新增：记录玩家的移动方向。默认 true 为向终点进发，false 为向起点折返
        public bool IsMovingForward { get; set; } = true; 
    }
}