using System;
using System.Collections.Generic;
using System.Linq;

namespace MilkyQQBot.Game;

public static class GameBalance
{
    public const int InitialGold = 10;

    public const int GoldCellMin = 2;
    public const int GoldCellMax = 4;

    public const int EncounterGoldMin = 1;
    public const int EncounterGoldMax = 3;
}

public static class GameSpecialCells
{
    // 金币格：15 个
    public static readonly HashSet<int> GoldSteps = new()
    {
        4, 10, 16, 22, 28, 34, 40, 46, 52, 58, 64, 70, 76, 82, 86
    };

    // 随机事件格：15 个
    public static readonly HashSet<int> RandomEventSteps = new()
    {
        7, 13, 19, 25, 31, 37, 43, 49, 55, 61, 67, 73, 79, 84, 88
    };
}

public enum GameEventTargetType
{
    Self,
    NearestAhead,
    NearestBehind,
    FrontMost,
    BackMost
}

public enum GameEventActionType
{
    AddGold,
    RemoveGold,
    MoveForward,
    MoveBackward
}

public sealed class GameRandomEventDef
{
    public int Id { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public GameEventTargetType TargetType { get; set; }
    public GameEventActionType ActionType { get; set; }
    public int MinValue { get; set; }
    public int MaxValue { get; set; }
}

public sealed class GameEventResult
{
    public string Message { get; set; } = string.Empty;
    public GameRandomEventDef? EventDef { get; set; }
    public int RolledValue { get; set; }
    public GamePlayer? TargetPlayer { get; set; }
}

public static class GameRandomEventLibrary
{
    public static readonly GameRandomEventDef[] All = new[]
    {
        // A. 丢金币类（1~25）
        Evt(1,  "你边走边装高手 😎，结果一头撞上树牌。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(2,  "你看见“免费”两个字就冲了过去，后面写的是“免费围观” 👀。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(3,  "你学别人单手转身，结果把钱袋甩进了草丛 🍃。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(4,  "你试图和路边的鹅讲道理 🪿，最终以赔款告终。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 4),
        Evt(5,  "你对着水坑整理发型 💁，风一吹，零钱先没了。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(6,  "你把石头认成烤地瓜 🍠，一口下去，治疗费到账。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 3),
        Evt(7,  "你被卖糖葫芦的小贩精准拿捏 🍡。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(8,  "你以为自己踩到了宝箱，结果是付费地砖 🧱。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 4),
        Evt(9,  "你围观别人吵架，莫名其妙被判了“吃瓜门票” 🍉。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(10, "你模仿吟游诗人弹空气琴 🎸，被收了表演场地费。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(11, "你看见树洞里闪闪发光，掏出来是别人丢的假金币 ✨。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(12, "你试图撸路边的小羊 🐑，小羊对你的发型提出了赔偿要求。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 3),
        Evt(13, "你踩到了村口新铺的泥巴地 👣，清洁费拿来。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(14, "你边走边唱，路边卖艺人说你抢了他饭碗 🎤。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 4),
        Evt(15, "你试图在桥边摆帅照 📸，结果手机差点进水。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(16, "你误把别人晾的被子当传送门，钻进去后被赶出来 🛏️。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(17, "你和青蛙对视三秒 🐸，输了还得请客。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(18, "你抽奖抽中了“再接再厉” 🎰。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 3),
        Evt(19, "你以为路边摊在送试吃，结果那是收费试吃 🍢。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(20, "你非说自己会轻功，落地的时候钱包先受伤了 🥲。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 4),
        Evt(21, "你蹲下系鞋带，鞋带没开，钱袋开了 👟。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(22, "你把招财猫当普通摆件拍了拍，结果触发“供奉系统” 🐱。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),
        Evt(23, "你试图驯服一只鸡 🐔，鸡反手收了培训费。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 2, 3),
        Evt(24, "你跟着香味一路跑，最后发现是别人家开饭 🍚。尴尬补偿。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 2),
        Evt(25, "你突然觉得自己今天挺帅，于是奖励了自己一杯饮料 🧋。", GameEventTargetType.Self, GameEventActionType.RemoveGold, 1, 3),

        // B. 捡金币类（26~45）
        Evt(26, "你在草丛里踢到一个小钱袋 💰。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(27, "你帮村民把滚走的菜篮子拦了下来 🧺。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(28, "你路过时扶正了歪掉的路牌 🪧，村民给你辛苦费。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(29, "你在树洞里发现松鼠忘记埋走的零钱 🐿️。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(30, "你帮小朋友捡回了风筝 🪁。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(31, "你认真听完了吟游诗人的整首歌 🎶，对方感动打赏。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(32, "你在桥边捡到一枚亮闪闪的铜币 ✨。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(33, "你帮摊主喊了一句“最后三串！” 🍢，意外分成。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(34, "你把迷路的小鸭送回了水边 🦆。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(35, "你路过时顺手把垃圾丢进桶里 🗑️，环保奖励到账。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(36, "你无意间猜中了路边小游戏答案 🎯。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(37, "你帮老奶奶提了两步东西 👵。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(38, "你在井边摸到了一枚旧铜板 🪙。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(39, "你被误认成街头艺人，居然真有人给赏钱 🎭。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(40, "你帮人把鸡追回来了 🐓。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(41, "你看到地上有红包，里面真有一点点钱 🧧。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(42, "你扶住了差点翻车的货车 🚚。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),
        Evt(43, "你无意中成了“今日幸运路人” 🍀。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(44, "你在石缝里抠出几枚零钱 🪨。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 2),
        Evt(45, "你安慰了一只看起来很失落的小狗 🐶，狗主人大方打赏。", GameEventTargetType.Self, GameEventActionType.AddGold, 1, 3),

        // C. 自己后退类（46~60）
        Evt(46, "你踩到一坨狗屎 💩，整个人灵魂后撤。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(47, "你试图飞跃水坑，成功掉回原位 🌊。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 3),
        Evt(48, "你被突然飞起的鸽子吓了一激灵 🕊️。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(49, "你踩到松果，脚下一滑 🌰。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(50, "你听见草丛里“沙沙”两声，决定战略撤退 🌿。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(51, "你模仿潜行大师，结果踩断了树枝 🥷。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(52, "你被自己的影子吓到了 👤。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 1),
        Evt(53, "你回头甩披风，披风把自己带回去了 🧥。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(54, "你刚摆好 pose，脚下小石头不同意 🪨。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 1),
        Evt(55, "你看见前面有一团可疑物体，身体比脑子更诚实 😨。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(56, "你在桥边装轻功高手，桥先笑出了声 🌉。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 3),
        Evt(57, "你低头看鞋，鞋没事，人退了 👟。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 1),
        Evt(58, "你被蚊子追着咬 🦟。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),
        Evt(59, "你突然想到自己刚才是不是忘关门了 🚪。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 1),
        Evt(60, "你走到一半发现地上的叶子长得很不对劲 🍂。", GameEventTargetType.Self, GameEventActionType.MoveBackward, 1, 2),

        // D. 自己前进类（61~70）
        Evt(61, "你听见前面好像有人喊“开饭啦” 🍚。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),
        Evt(62, "你被一阵主角 BGM 点燃了斗志 🎵。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),
        Evt(63, "你捡到一根特别顺手的木棍，走路都更有气势了 🪵。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 1),
        Evt(64, "你闻到了烤红薯的味道 🍠。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),
        Evt(65, "你脚下刚好踩到一块顺坡石板 🛹。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 1),
        Evt(66, "你突然觉得自己今天状态爆棚 ✨。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 1),
        Evt(67, "你听见前面有人说“这里有宝箱！” 📦。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),
        Evt(68, "你被风推了一把 🍃。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 1),
        Evt(69, "你脑补自己正在比赛冲线 🏁。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),
        Evt(70, "你想起晚饭还没着落，脚下不自觉加速了 🍜。", GameEventTargetType.Self, GameEventActionType.MoveForward, 1, 2),

        // E. 影响身后最近玩家（71~80）
        Evt(71, "你突然大喊“老师来了！” 📢。", GameEventTargetType.NearestBehind, GameEventActionType.MoveBackward, 1, 2),
        Evt(72, "你回头喊“前面发钱啦！” 💸。", GameEventTargetType.NearestBehind, GameEventActionType.MoveForward, 1, 2),
        Evt(73, "你故意把路边箭头拨歪了 ↩️。", GameEventTargetType.NearestBehind, GameEventActionType.MoveBackward, 1, 1),
        Evt(74, "你在地上画了个通往宝箱的箭头 🗺️。", GameEventTargetType.NearestBehind, GameEventActionType.MoveForward, 1, 1),
        Evt(75, "你唱歌严重跑调 🎤，后面的人想离你远一点。", GameEventTargetType.NearestBehind, GameEventActionType.MoveBackward, 1, 2),
        Evt(76, "你高喊“冲啊！” 🏃。", GameEventTargetType.NearestBehind, GameEventActionType.MoveForward, 1, 2),
        Evt(77, "你突然回头做了个鬼脸 👻。", GameEventTargetType.NearestBehind, GameEventActionType.MoveBackward, 1, 1),
        Evt(78, "你说“我刚刚踩到金币格了！” 🪙。", GameEventTargetType.NearestBehind, GameEventActionType.MoveForward, 1, 1),
        Evt(79, "你小声说“后面有鹅……” 🪿。", GameEventTargetType.NearestBehind, GameEventActionType.MoveBackward, 1, 2),
        Evt(80, "你朝后面扔了颗松果 🌰，大家一下都精神了。", GameEventTargetType.NearestBehind, GameEventActionType.MoveForward, 1, 1),

        // F. 影响身前最近玩家（81~90）
        Evt(81, "你冲着前面喊“兄弟你鞋带开了！” 👟。", GameEventTargetType.NearestAhead, GameEventActionType.MoveBackward, 1, 1),
        Evt(82, "你大喊“前面那个！你掉钱啦！” 💰。", GameEventTargetType.NearestAhead, GameEventActionType.MoveBackward, 1, 2),
        Evt(83, "你说“快跑，后面有人追来了！” 😱。", GameEventTargetType.NearestAhead, GameEventActionType.MoveForward, 1, 2),
        Evt(84, "你朝前面喊“宝箱刷新啦！” 📦。", GameEventTargetType.NearestAhead, GameEventActionType.MoveForward, 1, 1),
        Evt(85, "你学狼嚎 🐺，把前面的人吓麻了。", GameEventTargetType.NearestAhead, GameEventActionType.MoveBackward, 1, 2),
        Evt(86, "你喊“前面有免费饮料！” 🧋。", GameEventTargetType.NearestAhead, GameEventActionType.MoveForward, 1, 2),
        Evt(87, "你扔出一句“你后面有虫！” 🐛。", GameEventTargetType.NearestAhead, GameEventActionType.MoveBackward, 1, 1),
        Evt(88, "你高喊“冲刺阶段开始！” 🏁。", GameEventTargetType.NearestAhead, GameEventActionType.MoveForward, 1, 1),
        Evt(89, "你突然说“前面那棵树会说话” 🌳。", GameEventTargetType.NearestAhead, GameEventActionType.MoveBackward, 1, 1),
        Evt(90, "你喊“终点前有隐藏奖励！” 🎁。", GameEventTargetType.NearestAhead, GameEventActionType.MoveForward, 1, 2),

        // G. 影响最前方 / 最后方玩家（91~100）
        Evt(91,  "村里的税务鹅盯上了最前方玩家 🪿。", GameEventTargetType.FrontMost, GameEventActionType.RemoveGold, 1, 3),
        Evt(92,  "最后方玩家捡到一枚安慰奖铜币 🪙。", GameEventTargetType.BackMost, GameEventActionType.AddGold, 1, 2),
        Evt(93,  "天降一阵怪风 🌪️，最前方玩家被吹得踉跄。", GameEventTargetType.FrontMost, GameEventActionType.MoveBackward, 1, 1),
        Evt(94,  "最后方玩家触发“别灰心”鼓励 Buff 💪。", GameEventTargetType.BackMost, GameEventActionType.MoveForward, 1, 1),
        Evt(95,  "最前方玩家太嚣张，被路边摊老板强行推销 🍢。", GameEventTargetType.FrontMost, GameEventActionType.RemoveGold, 1, 2),
        Evt(96,  "最后方玩家获得“追赶者补贴” 🧧。", GameEventTargetType.BackMost, GameEventActionType.AddGold, 1, 3),
        Evt(97,  "最前方玩家踩到香蕉皮 🍌。", GameEventTargetType.FrontMost, GameEventActionType.MoveBackward, 1, 2),
        Evt(98,  "最后方玩家突然灵感爆发，脚下生风 ✨。", GameEventTargetType.BackMost, GameEventActionType.MoveForward, 1, 2),
        Evt(99,  "最前方玩家被围观群众要求发表获奖感言 🎤，耽误了脚步。", GameEventTargetType.FrontMost, GameEventActionType.MoveBackward, 1, 1),
        Evt(100, "最后方玩家触发“别摆了，快追！”表情包激励 😂。", GameEventTargetType.BackMost, GameEventActionType.MoveForward, 1, 1),
    };

    private static GameRandomEventDef Evt(
        int id,
        string scenario,
        GameEventTargetType targetType,
        GameEventActionType actionType,
        int minValue,
        int maxValue)
    {
        return new GameRandomEventDef
        {
            Id = id,
            Scenario = scenario,
            TargetType = targetType,
            ActionType = actionType,
            MinValue = minValue,
            MaxValue = maxValue
        };
    }
}

public static class GameEventEngine
{
    private static readonly Random Rng = new();

    public static string TriggerGoldCell(GamePlayer player)
    {
        int gold = Rng.Next(GameBalance.GoldCellMin, GameBalance.GoldCellMax + 1);
        player.Gold += gold;
        return $"🪙 你踩到了金币格，捡到了 {gold} 枚金币！";
    }

    public static GameEventResult TriggerRandomEvent(GamePlayer triggerPlayer, List<GamePlayer> allPlayersInGroup)
    {
        var def = GameRandomEventLibrary.All[Rng.Next(GameRandomEventLibrary.All.Length)];
        int rolledValue = Rng.Next(def.MinValue, def.MaxValue + 1);

        GamePlayer? target = ResolveTarget(triggerPlayer, allPlayersInGroup, def.TargetType);

        if (def.TargetType != GameEventTargetType.Self && target == null)
        {
            return new GameEventResult
            {
                EventDef = def,
                RolledValue = 0,
                TargetPlayer = null,
                Message = $"{def.Scenario} 🤷 但现在没有合适的目标，什么也没有发生。"
            };
        }

        target ??= triggerPlayer;

        int actualValue = ApplyEffect(target, def.ActionType, rolledValue);

        string targetText = BuildTargetText(triggerPlayer, target, def.TargetType);
        string resultText = BuildResultText(triggerPlayer, target, def.ActionType, actualValue, targetText);

        return new GameEventResult
        {
            EventDef = def,
            RolledValue = actualValue,
            TargetPlayer = target,
            Message = $"{def.Scenario} {resultText}"
        };
    }

    public static string? TryTriggerEncounter(GamePlayer triggerPlayer, List<GamePlayer> allPlayersInGroup)
    {
        var others = allPlayersInGroup
            .Where(p => p.UserId != triggerPlayer.UserId && p.Step == triggerPlayer.Step)
            .ToList();

        if (others.Count == 0)
        {
            return null;
        }

        var target = others[Rng.Next(others.Count)];
        int amount = Rng.Next(GameBalance.EncounterGoldMin, GameBalance.EncounterGoldMax + 1);
        int actual = Math.Min(triggerPlayer.Gold, amount);

        if (actual > 0)
        {
            triggerPlayer.Gold -= actual;
            target.Gold += actual;
            return $"🤝 你遇见了 {DisplayName(target)}，交出了 {actual} 枚金币。";
        }

        return $"🤝 你遇见了 {DisplayName(target)}，但你穷得叮当响，一枚金币也掏不出来。";
    }

    private static GamePlayer? ResolveTarget(GamePlayer triggerPlayer, List<GamePlayer> players, GameEventTargetType targetType)
    {
        return targetType switch
        {
            GameEventTargetType.Self => triggerPlayer,
            GameEventTargetType.NearestAhead => FindNearestAhead(triggerPlayer, players),
            GameEventTargetType.NearestBehind => FindNearestBehind(triggerPlayer, players),
            GameEventTargetType.FrontMost => FindFrontMost(players),
            GameEventTargetType.BackMost => FindBackMost(players),
            _ => null
        };
    }

    private static GamePlayer? FindNearestAhead(GamePlayer triggerPlayer, List<GamePlayer> players)
    {
        var candidates = players
            .Where(p => p.UserId != triggerPlayer.UserId && p.Step > triggerPlayer.Step)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        int minGap = candidates.Min(p => p.Step - triggerPlayer.Step);
        var nearest = candidates.Where(p => p.Step - triggerPlayer.Step == minGap).ToList();
        return nearest[Rng.Next(nearest.Count)];
    }

    private static GamePlayer? FindNearestBehind(GamePlayer triggerPlayer, List<GamePlayer> players)
    {
        var candidates = players
            .Where(p => p.UserId != triggerPlayer.UserId && p.Step < triggerPlayer.Step)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        int minGap = candidates.Min(p => triggerPlayer.Step - p.Step);
        var nearest = candidates.Where(p => triggerPlayer.Step - p.Step == minGap).ToList();
        return nearest[Rng.Next(nearest.Count)];
    }

    private static GamePlayer? FindFrontMost(List<GamePlayer> players)
    {
        if (players.Count == 0)
        {
            return null;
        }

        int maxStep = players.Max(p => p.Step);
        var candidates = players.Where(p => p.Step == maxStep).ToList();
        return candidates[Rng.Next(candidates.Count)];
    }

    private static GamePlayer? FindBackMost(List<GamePlayer> players)
    {
        if (players.Count == 0)
        {
            return null;
        }

        int minStep = players.Min(p => p.Step);
        var candidates = players.Where(p => p.Step == minStep).ToList();
        return candidates[Rng.Next(candidates.Count)];
    }

    private static int ApplyEffect(GamePlayer target, GameEventActionType actionType, int value)
    {
        switch (actionType)
        {
            case GameEventActionType.AddGold:
                target.Gold += value;
                return value;

            case GameEventActionType.RemoveGold:
                int actualLost = Math.Min(target.Gold, value);
                target.Gold -= actualLost;
                return actualLost;

            case GameEventActionType.MoveForward:
                MoveForward(target, value);
                return value;

            case GameEventActionType.MoveBackward:
                MoveBackward(target, value);
                return value;

            default:
                return 0;
        }
    }

    private static string BuildTargetText(GamePlayer triggerPlayer, GamePlayer target, GameEventTargetType targetType)
    {
        if (target.UserId == triggerPlayer.UserId)
        {
            return "你";
        }

        return targetType switch
        {
            GameEventTargetType.NearestAhead => $"身前最近的玩家【{DisplayName(target)}】",
            GameEventTargetType.NearestBehind => $"身后最近的玩家【{DisplayName(target)}】",
            GameEventTargetType.FrontMost => $"最前方玩家【{DisplayName(target)}】",
            GameEventTargetType.BackMost => $"最后方玩家【{DisplayName(target)}】",
            _ => $"玩家【{DisplayName(target)}】"
        };
    }

    private static string BuildResultText(
        GamePlayer triggerPlayer,
        GamePlayer target,
        GameEventActionType actionType,
        int actualValue,
        string targetText)
    {
        bool isSelf = target.UserId == triggerPlayer.UserId;

        return actionType switch
        {
            GameEventActionType.AddGold =>
                isSelf
                    ? $"🪙 你获得了 {actualValue} 枚金币。"
                    : $"🪙 {targetText}获得了 {actualValue} 枚金币。",

            GameEventActionType.RemoveGold =>
                actualValue <= 0
                    ? (isSelf
                        ? "😮 但你一枚金币也没有，什么都没掉。"
                        : $"😮 但{targetText}一枚金币也没有，什么都没掉。")
                    : (isSelf
                        ? $"💸 你失去了 {actualValue} 枚金币。"
                        : $"💸 {targetText}失去了 {actualValue} 枚金币。"),

            GameEventActionType.MoveForward =>
                isSelf
                    ? $"⏩ 你前进了 {actualValue} 步。"
                    : $"⏩ {targetText}前进了 {actualValue} 步。",

            GameEventActionType.MoveBackward =>
                isSelf
                    ? $"⏪ 你后退了 {actualValue} 步。"
                    : $"⏪ {targetText}后退了 {actualValue} 步。",

            _ => "🤷 什么都没有发生。"
        };
    }

    private static string DisplayName(GamePlayer player)
    {
        return string.IsNullOrWhiteSpace(player.Nickname)
            ? player.UserId.ToString()
            : player.Nickname;
    }

    // 前进：沿当前方向移动，遇到终点自动折返
    private static void MoveForward(GamePlayer player, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            if (player.Direction >= 0)
            {
                if (player.Step >= GameMapData.MaxStep)
                {
                    player.Direction = -1;
                    player.Step = Math.Max(0, player.Step - 1);
                }
                else
                {
                    player.Step++;
                }
            }
            else
            {
                if (player.Step <= 0)
                {
                    player.Direction = 1;
                    player.Step = Math.Min(GameMapData.MaxStep, player.Step + 1);
                }
                else
                {
                    player.Step--;
                }
            }
        }
    }

    // 后退：按当前方向的反方向退回去；到边界时不再继续退
    private static void MoveBackward(GamePlayer player, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            if (player.Direction >= 0)
            {
                if (player.Step > 0)
                {
                    player.Step--;
                }
            }
            else
            {
                if (player.Step < GameMapData.MaxStep)
                {
                    player.Step++;
                }
            }
        }
    }
}