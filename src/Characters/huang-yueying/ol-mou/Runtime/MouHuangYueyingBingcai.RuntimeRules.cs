using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huang_yueying_ol_mou_MouHuangYueyingBingcai
{
    internal static CardConversionSource MouHuangYueyingSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static bool IsMouHuangYueyingDamageCardKind(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) || kind is CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.FireAttack or CardKind.WumiaoFlood or CardKind.UnexpectedAssault;
}
