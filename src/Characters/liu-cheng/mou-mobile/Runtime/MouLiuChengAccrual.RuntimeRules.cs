namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_cheng_mou_mobile_MouLiuChengAccrual
{
    internal static bool SameMouLiuChengAccrual(MouLiuChengAccrualClaim? a, MouLiuChengAccrualClaim? b) =>
        a is not null && b is not null && a.Source == b.Source && a.GameplayHash == b.GameplayHash && a.UseId == b.UseId &&
        a.ActionId == b.ActionId && a.ActorSeat == b.ActorSeat && a.EffectiveKind == b.EffectiveKind && a.Phase == b.Phase &&
        a.NotificationOrdinal == b.NotificationOrdinal && a.Targets.SequenceEqual(b.Targets);
}
