using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiSunJunProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-xiongtu" or "mobile:shi-xianshuai") || _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-xianshuai")
        {
            var suits = new[] { (Suit.Spade, "♠"), (Suit.Heart, "♥"), (Suit.Club, "♣"), (Suit.Diamond, "♦") };
            bool Used(Suit suit) => states.FirstOrDefault(s => s.StateId == "xianshuai-suit-" + suit)?.Value == 1;
            var ownTurn = states.FirstOrDefault(s => s.StateId == "xianshuai-own-turn")?.Value == 1;
            return skill with { ProgressText = ownTurn ? $"已用 {suits.Count(s => Used(s.Item1))}/4 色" : "回合外",
                StateText = string.Join("  ", suits.Select(s => s.Item2 + (Used(s.Item1) ? "已用" : "未用"))) + "\n每种花色仅本回合首次手牌使用免次数；后续同色牌仍占正常次数。" };
        }
        var quota = states.FirstOrDefault(s => s.StateId == "show-hand");
        var x = states.FirstOrDefault(s => s.StateId == "xiongtu-cost")?.Value ?? 4;
        var armed = states.FirstOrDefault(s => s.StateId == "xiongtu-armed")?.Value == 1;
        return skill with { ProgressText = $"本阶段 {quota?.Used ?? 0}/{quota?.Value ?? 1}",
            StateText = $"当前未进入弃牌堆的花色数 X={x}；选择分支时固定费用。\n" +
                (armed ? "本回合你经凶图以外途径每造成一次伤害后摸1张牌，并将该实际阶段凶图上限改为2次。" : "发动凶图后开启本回合其他来源伤害的摸牌收益。") };
    }
}
