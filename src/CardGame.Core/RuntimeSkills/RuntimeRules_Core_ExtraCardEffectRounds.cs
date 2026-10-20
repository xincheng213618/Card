namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ExtraCardEffectRounds
{
    internal static CardAttackState ResetExtraCardEffectAttack(CardAttackState original) => original with {
        Active = true, CixiongDoubleSwordsResolved = true, DamageWasApplied = false, DamageAmountFinalized = false,
        BeforeDamageProgramsResolved = false, ResolvedDyingDamageFrameId = null, ChainedTargetSeats = [], ChainedTargetIndex = 0,
        IsChainPropagation = false, SuccessfulDodgeResponses = 0, IceSwordAttempted = false, QilinBowAttempted = false,
        FactionDefenseAttempted = false, FactionSlashAttempted = false, DamageRedirected = false, RecipientDamageScope = null,
        DamageTransferFollowup = null
    };
}
