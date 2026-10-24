namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyBoundaryZhuZhiProgress(HumanSkillViewModel skill)
    {
        if(skill.ContentId!="boundary:anguo-mobile"||_snapshot is not{}snapshot||snapshot.Players.SingleOrDefault(p=>p.Seat==snapshot.HumanSeat)?.SkillRuntimeStates?
            .SingleOrDefault(s=>s.SkillId==skill.ContentId)is not{}runtime)return skill;
        var states=runtime.PublicRuleStates??[];
        var marked=states.FirstOrDefault(s=>s.StateId=="anguo-marked")?.Value??0;
        var unused=states.FirstOrDefault(s=>s.StateId=="anguo-unused")?.Value??0;
        return skill with{ProgressText=$"标记 {marked}",StateText=$"场上安国标记持有者 {marked} 人；尚未获得过标记的其他角色 {unused} 人。\n持标记者手牌上限等于体力上限；未持标记来源的致命伤害可被防止。"};
    }
}
