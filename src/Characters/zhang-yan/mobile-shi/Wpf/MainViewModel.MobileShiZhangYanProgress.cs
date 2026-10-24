using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiZhangYanProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-feijing" or "mobile:shi-xiaoge")) return skill;
        var states = _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId)?.PublicRuleStates ?? [];
        int Value(string id) => states.FirstOrDefault(s => s.StateId == id)?.Value ?? 0;
        return skill.ContentId == "mobile:shi-feijing"
            ? skill with { ProgressText = $"本回合 {Value("feijing-used")}/2", StateText = "伤害类锦囊可当杀使用或打出。路径不含你和原目标；各角色先私下选牌，全部完成后同时展示，再依次弃置。" +
                (Value("feijing-path-count") > 0 ? $"\n本路径 {Value("feijing-path-count")} 人，已完成 {Value("feijing-committed")} 人的承诺。" : string.Empty) }
            : skill with { ProgressText = $"防伤{Value("xiaoge-prevented")} · 决斗{Value("xiaoge-duels")}", StateText = "飞径额外目标将受到此杀伤害时防止之，你回复1点体力并获得其原弃牌。此杀最终只有一个指定目标时，结算结束后再对其视为使用决斗。" };
    }
}
