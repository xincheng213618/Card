namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyBoundaryQuanCongProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId != "boundary:yaoming-mobile" ||
            _snapshot?.Players.SingleOrDefault(p => p.Seat == _snapshot.HumanSeat)?.SkillRuntimeStates?
                .SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        var remaining = states.FirstOrDefault(s => s.StateId == "yaoming-remaining")?.Value ?? 2;
        var option = states.FirstOrDefault(s => s.StateId == "yaoming-pending-option")?.Value ?? 0;
        var record = option == 1 ? "上次：弃牌" : option == 2 ? "上次：摸牌" : "未记录选项";
        return skill with
        {
            ProgressText = $"剩余 {remaining}/4",
            StateText = $"{record}。选择不同选项返还1次并清除记录。\n每受到1点实际伤害补充1次，上限4次。"
        };
    }
}
