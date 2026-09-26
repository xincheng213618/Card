namespace CardGame.Core;

/// <summary>Resources supplied by a rules boundary, independent of skill identity.</summary>
[Flags]
internal enum ProgramContextCapability
{
    None = 0,
    DrawPlan = 1,
    Damage = 2,
    PhaseInsertion = 4,
    Judgment = 8,
    TurnEffects = 16,
    CardAction = 32,
    Pindian = 64,
    Death = 128,
    Dying = 256
}

internal static class ProgramEntryCapabilities
{
    internal const ProgramContextCapability Common = ProgramContextCapability.TurnEffects;

    internal static bool UsesSharedExecutor(SkillProgramTriggerWindow window) => window is
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
        SkillProgramTriggerWindow.DrawPhaseStarting or
        SkillProgramTriggerWindow.SelfDyingResponse or
        SkillProgramTriggerWindow.DyingResponse or
        SkillProgramTriggerWindow.BeforeDamageApplied or
        SkillProgramTriggerWindow.DamageAppliedBeforeDying or
        SkillProgramTriggerWindow.AfterDamageApplied or
        SkillProgramTriggerWindow.PlayEnding or
        SkillProgramTriggerWindow.TurnEnding or
        SkillProgramTriggerWindow.CardsMoved or
        SkillProgramTriggerWindow.OwnerDied or
        SkillProgramTriggerWindow.CardUseCommitted or
        SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
        SkillProgramTriggerWindow.CardUseTargetsFinalized or
        SkillProgramTriggerWindow.CardResponseAccepted or
        SkillProgramTriggerWindow.CardUseCompleted;

    internal static ProgramContextCapability For(SkillProgramTriggerWindow? window) => window switch
    {
        null => Common | ProgramContextCapability.Judgment | ProgramContextCapability.Pindian,
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => Common | ProgramContextCapability.PhaseInsertion | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.DrawPhaseStarting => Common | ProgramContextCapability.DrawPlan | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.TurnEnding => Common | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.BeforeDamageApplied or
            SkillProgramTriggerWindow.DamageAppliedBeforeDying or
            SkillProgramTriggerWindow.AfterDamageApplied =>
            Common | ProgramContextCapability.Damage | ProgramContextCapability.Pindian,
        SkillProgramTriggerWindow.OwnerDied =>
            Common | ProgramContextCapability.Judgment | ProgramContextCapability.Death,
        SkillProgramTriggerWindow.DyingResponse => Common | ProgramContextCapability.Dying,
        SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
        SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
        SkillProgramTriggerWindow.CardUseCompleted =>
            Common | ProgramContextCapability.CardAction,
        _ when UsesSharedExecutor(window.Value) => Common,
        _ => throw new InvalidOperationException($"Window '{window}' has no shared program-frame adapter.")
    };
}
