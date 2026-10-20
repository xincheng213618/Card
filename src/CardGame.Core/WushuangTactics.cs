namespace CardGame.Core;

public sealed record WushuangTactic(int Id, string Name, string Description, int Cost, int Quality, string? SkillId = null);
public static class WushuangTactics
{
    public static IReadOnlyList<WushuangTactic> All { get; } = Array.AsReadOnly(new WushuangTactic[]
    {
        new(2011, "锦囊计", "手牌上限+X（X为本回合摸牌阶段摸牌数的一半）。", 2, 2, "shanhe:current-jinnangji-2011"),
        new(2051, "连击Ⅰ", "出牌阶段，你的出杀次数+1。", 2, 2, "shanhe:current-lianji-2051"),
        new(2052, "连击Ⅱ", "出牌阶段，你的出杀次数+3。", 4, 4, "shanhe:current-lianji-2052"),
        new(2082, "多多益善", "每回合你第5次摸牌后，你摸1张牌。", 1, 1, "shanhe:current-duoduo-yishan-2082"),
        new(2083, "多多益善Ⅱ", "每回合你第3次摸牌后，你摸1张牌。", 2, 2, "shanhe:current-duoduo-yishan-2083"),
        new(2084, "多多益善Ⅲ", "每回合你第1次摸牌后，你摸2张牌。", 4, 4, "shanhe:current-duoduo-yishan-2084"),
        new(2191, "药理Ⅴ", "回复体力时，额外回复1点。", 3, 3, "shanhe:current-yaoli-2191"),
        new(2192, "药理Ⅵ", "回复体力时，额外回复2点。", 4, 4, "shanhe:current-yaoli-2192"),
        new(2307, "赌术Ⅰ", "你的拼点牌点数+3（最大为K）。", 1, 1, "shanhe:dushu-1"),
        new(2319, "药理", "每回合首次回复体力时，额外回复1点。", 3, 3, "shanhe:current-yaoli-2319"),
        new(2320, "药理Ⅱ", "每回合前3次回复体力时，额外回复1点。", 4, 4, "shanhe:current-yaoli-2320"),
        new(2036, "当头一棒", "每轮，你的首张【杀】伤害+1。", 3, 3, "shanhe:current-dangtou-2036"),
        new(2037, "当头一棒Ⅱ", "每轮，你的首张【杀】伤害+2。", 4, 4, "shanhe:current-dangtou-2037"),
        new(2100, "雷火势Ⅰ", "每回合，你使用的第一张属性【杀】伤害+1。", 2, 2, "shanhe:current-leihuo-2100"),
        new(2101, "雷火势Ⅱ", "每回合，你使用的第一张属性【杀】伤害+2。", 4, 4, "shanhe:current-leihuo-2101"),
        new(2303, "弱袭", "你的手牌数小于体力值时，造成的伤害+1。", 3, 3, "shanhe:ruoxi"),
        new(2312, "妙技Ⅰ", "每回合首张锦囊造成的伤害+1。", 3, 3, "shanhe:miaoji-1"),
        new(2313, "妙技Ⅱ", "每回合前两张锦囊造成的伤害+1。", 4, 4, "shanhe:miaoji-2"),
        new(2315, "噬血Ⅰ", "回复体力后，摸1张牌。", 4, 4, "shanhe:shixue-1"),
        new(2317, "反刺", "每回合首次受到伤害后，对所有敌方造成1点伤害。", 4, 4, "shanhe:fanci"),
        new(2342, "搏命", "手牌上限-2，出杀次数+1。", 1, 1, "wushuang:boming"),
        new(2285, "兵权Ⅰ", "自己的自然回合获得的虎符数+1。", 3, 3),
        new(2286, "兵权Ⅱ", "每个自然回合获得的虎符数+1。", 4, 4),
        new(2366, "兵权Ⅲ", "每回合获得虎符的数量翻倍。", 4, 4)
    });
    public static IReadOnlyDictionary<int, WushuangTactic> ById { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<int, WushuangTactic>(All.ToDictionary(t => t.Id));
}
