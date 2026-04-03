namespace MilkyQQBot;

public class UserActivityStat
{
    public long SenderId { get; set; }
    public string Nickname { get; set; }
    public int MessageCount { get; set; }
    public int WordCount { get; set; }
    public int ImageCount { get; set; }
    public int FaceCount { get; set; }
}