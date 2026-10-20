namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_FinalTargetSlash
{
    internal static bool HasFinalTargetSlashEffects(IEnumerable<SkillProgramEffect> effects) =>
        effects.Any(effect => effect.Op is SkillProgramEffectOp.PreventCurrentTargetSlashCancellation or SkillProgramEffectOp.AddCurrentTargetSlashDamage or
            SkillProgramEffectOp.ZhuiLieEscalateTargetDamage or SkillProgramEffectOp.RevealRecordedSuitSlash or
            SkillProgramEffectOp.ClaimCurrentUsePhysicalCards or SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule or
            SkillProgramEffectOp.ResolveMouTianYuZhenxi);

    internal static bool MatchesFinalTargetComparison(ProgramFinalTargetComparison comparison, SkillProgramTriggerFacts facts) => comparison switch
    {
        ProgramFinalTargetComparison.TargetHandAtMostActor => facts.EventTargetHandCount <= facts.CurrentHandCount,
        ProgramFinalTargetComparison.TargetHpAtLeastActor => facts.EventTargetHp >= facts.CurrentHp,
        ProgramFinalTargetComparison.Always => true,
        _ => throw new InvalidOperationException("Unknown frozen final target comparison.")
    };
}
