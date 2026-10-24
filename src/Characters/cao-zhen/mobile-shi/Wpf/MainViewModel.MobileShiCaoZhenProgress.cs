using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiCaoZhenProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId != "mobile:shi-sifeng") return skill;
        var known = _snapshot?.Players.SelectMany(p => (p.NamedAmbushPiles ?? []).Where(pile => pile.SkillId == "mobile:shi-sifeng" && pile.Entries is not null)
            .Select(pile => (Name: p.Name, Count: pile.Count))).ToArray() ?? [];
        return skill with { ProgressText = known.Length == 0 ? "待布置" : $"伺锋 {known.Sum(p => p.Count)}张", StateText = known.Length == 0
            ? "结束阶段观看真实牌堆顶3张，按任意顺序扣置给其他角色。每人的队列按放置顺序消耗；暗牌仅布置者知道。"
            : string.Join("；", known.Select(p => $"{p.Name}：{p.Count}张")) + "。其自己的真实回合内每次使用牌结束后移去首张，比颜色；不同则其弃1张手牌。回合结束可获得剩余牌，或移去后造成1点伤害。" };
    }
}
