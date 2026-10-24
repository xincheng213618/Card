namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyBoundaryCaoZhenSidiProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId != "boundary:sidi-mobile") return skill;
        var records = _snapshot.BoundarySidiPredictions ?? [];
        var details = records.Count == 0 ? "当前没有尚未验证的秘密预测。" :
            string.Join("；", records.Select(record =>
                $"{_snapshot.Players.Single(player => player.Seat == record.HolderSeat).Name} → {_snapshot.Players.Single(player => player.Seat == record.TargetSeat).Name}"));
        return skill with { ProgressText = $"预测 {records.Count}", StateText = skill.StateText + "\n仅你可见：" + details };
    }
}
