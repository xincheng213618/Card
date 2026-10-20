namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ke_bi_neng_ol_KeBiNengNativeReturns
{
    internal static bool SameKeBiNengAction(LegalAction a, LegalAction b) => a.Kind == b.Kind && a.CardId == b.CardId && a.TargetSeat == b.TargetSeat &&
        a.PlayedCardKind == b.PlayedCardKind && a.TargetCardId == b.TargetCardId && a.ConversionSource == b.ConversionSource && a.EquipmentKind == b.EquipmentKind &&
        a.ProgramSkillId == b.ProgramSkillId && a.ProgramActivationId == b.ProgramActivationId && a.RoundOnceCardlessBasicUse == b.RoundOnceCardlessBasicUse &&
        a.TieredRoundZeroUse == b.TieredRoundZeroUse && a.DrawFundedDistinctBasicUse == b.DrawFundedDistinctBasicUse && a.ChainedStateBasicUse == b.ChainedStateBasicUse &&
        a.TargetSeats.SequenceEqual(b.TargetSeats) && CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNeng.KeBiNengMaterials(a).SequenceEqual(CardGame.Core.RuntimeSkills.RuntimeRules_ke_bi_neng_ol_KeBiNeng.KeBiNengMaterials(b)) && (a.AdditionalConversionSources ?? []).SequenceEqual(b.AdditionalConversionSources ?? []);
}
