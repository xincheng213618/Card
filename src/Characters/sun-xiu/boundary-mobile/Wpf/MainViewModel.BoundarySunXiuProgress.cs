namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyBoundarySunXiuProgress(HumanSkillViewModel skill)
    {
        if(skill.ContentId is not("boundary:xingxue-mobile"or"boundary:xingxue-mobile-upgraded")||_snapshot is not{}snapshot||
            snapshot.Players.SingleOrDefault(p=>p.Seat==snapshot.HumanSeat)is not{}owner)return skill;
        var upgraded=skill.ContentId=="boundary:xingxue-mobile-upgraded";var quota=Math.Max(0,upgraded?owner.MaxHp:owner.Hp);
        return skill with{ProgressText=upgraded?"2级":"1级",StateText=$"当前至多选择 {quota} 名角色，按实际行动顺序依次摸牌。\n"+
            (upgraded?"每人将一张手牌或装备牌置顶，或交给本次其他入选角色。":"每人将一张手牌或装备牌置于牌堆顶。")};
    }
}
