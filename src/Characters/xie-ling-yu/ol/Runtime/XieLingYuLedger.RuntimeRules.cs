namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_xie_ling_yu_ol_XieLingYuLedger
{
    internal static CardConversionSource XieLingYuSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static int XieLingYuNameBit(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(kind) switch { CardKind.Slash => 1, CardKind.Dodge => 2, CardKind.Peach => 4, CardKind.Alcohol => 8, _ => 0 };

    internal static CardKind[] XieLingYuNames(int mask) => new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol }.Where(k => (mask & CardGame.Core.RuntimeSkills.RuntimeRules_xie_ling_yu_ol_XieLingYuLedger.XieLingYuNameBit(k)) != 0).ToArray();
}
