using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiLuSuProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-haoshi" or "mobile:shi-dimeng") || _snapshot is null) return skill;
        if (skill.ContentId == "mobile:shi-dimeng") return skill with { StateText = "选择两名手牌数之差至多3的角色，可以包含自己。先交换，再选较少者摸X或自己多选弃X；X为已损失体力，不足全弃。" };
        var states = _snapshot.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId)?.PublicRuleStates ?? [];
        var target = states.FirstOrDefault(s => s.StateId == "haoshi-beneficiary"); var refill = states.FirstOrDefault(s => s.StateId == "haoshi-refill");
        var name = target is null ? null : _snapshot.Players.SingleOrDefault(p => p.Seat == target.Value)?.Name;
        return skill with { ProgressText = name is null ? "待授权" : $"补牌 {refill?.Used ?? 0}/2", StateText = name is null
            ? "结束阶段选择一名其他角色，直到下个回合开始，其可如手牌使用或打出你的手牌。前两次因此失去最后手牌，补至3张。"
            : $"好施已授权：{name}。持续至你的下个回合开始；已补牌{refill?.Used ?? 0}/2次，每次补至3张。实体牌始终留在你的手牌区。" };
    }
}
