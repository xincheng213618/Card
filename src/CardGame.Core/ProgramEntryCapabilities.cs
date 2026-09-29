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
    Dying = 256,
    JudgmentReplacement = 512,
    PhaseSubstitution = 1024,
    PhaseOwner = 2048
}

internal static class ProgramEntryCapabilities
{
    internal const ProgramContextCapability Common = ProgramContextCapability.TurnEffects;

    internal static bool SupportsWindow(SkillProgramTriggerWindow window) => window is
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or
        SkillProgramTriggerWindow.DrawPhaseStarting or
        SkillProgramTriggerWindow.AfterNormalDraw or
        SkillProgramTriggerWindow.SelfDyingResponse or
        SkillProgramTriggerWindow.DyingResponse or
        SkillProgramTriggerWindow.BeforeDamageApplied or
        SkillProgramTriggerWindow.DamageAppliedBeforeDying or
        SkillProgramTriggerWindow.AfterDamageApplied or
        SkillProgramTriggerWindow.PlayEnding or
        SkillProgramTriggerWindow.DiscardPhaseStarting or
        SkillProgramTriggerWindow.DiscardPhaseEnded or
        SkillProgramTriggerWindow.TurnEnding or
        SkillProgramTriggerWindow.PlayPhaseStarting or
        SkillProgramTriggerWindow.CardsMoved or
        SkillProgramTriggerWindow.CardsGained or
        SkillProgramTriggerWindow.DiscardPileReceived or
        SkillProgramTriggerWindow.AfterHpLost or
        SkillProgramTriggerWindow.AfterHpRecovered or
        SkillProgramTriggerWindow.OwnerDied or
        SkillProgramTriggerWindow.CharacterDied or
        SkillProgramTriggerWindow.CardUseCommitted or
        SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
        SkillProgramTriggerWindow.CardUseTargetsFinalized or
        SkillProgramTriggerWindow.CardResponseAccepted or
        SkillProgramTriggerWindow.CardUseCompleted or
        SkillProgramTriggerWindow.SlashTargetRedirecting or
        SkillProgramTriggerWindow.SlashBeforeResponse or
        SkillProgramTriggerWindow.SlashFullyDodged or
        SkillProgramTriggerWindow.JudgmentReplacing or
        SkillProgramTriggerWindow.JudgmentFinalized;

    internal static ProgramContextCapability For(SkillProgramTriggerWindow? window) => window switch
    {
        null => Common | ProgramContextCapability.Judgment | ProgramContextCapability.Pindian,
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => Common | ProgramContextCapability.PhaseInsertion | ProgramContextCapability.PhaseSubstitution | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.DrawPhaseStarting => Common | ProgramContextCapability.DrawPlan | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.AfterNormalDraw => Common | ProgramContextCapability.PhaseSubstitution | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.DiscardPhaseStarting => Common | ProgramContextCapability.PhaseSubstitution | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.DiscardPhaseEnded => Common | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.PlayEnding or SkillProgramTriggerWindow.TurnEnding => Common | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.PlayPhaseStarting =>
            Common | ProgramContextCapability.Judgment | ProgramContextCapability.PhaseOwner,
        SkillProgramTriggerWindow.BeforeDamageApplied or
            SkillProgramTriggerWindow.DamageAppliedBeforeDying or
            SkillProgramTriggerWindow.AfterDamageApplied =>
            Common | ProgramContextCapability.Damage | ProgramContextCapability.Pindian |
            ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or
        SkillProgramTriggerWindow.DiscardPileReceived =>
            Common | ProgramContextCapability.Judgment,
        SkillProgramTriggerWindow.OwnerDied =>
            Common | ProgramContextCapability.Judgment | ProgramContextCapability.Death,
        SkillProgramTriggerWindow.CharacterDied => Common | ProgramContextCapability.Death,
        SkillProgramTriggerWindow.JudgmentReplacing =>
            Common | ProgramContextCapability.Judgment | ProgramContextCapability.JudgmentReplacement,
        SkillProgramTriggerWindow.JudgmentFinalized =>
            Common | ProgramContextCapability.Judgment | ProgramContextCapability.Damage,
        SkillProgramTriggerWindow.SelfDyingResponse or SkillProgramTriggerWindow.DyingResponse =>
            Common | ProgramContextCapability.Dying,
        SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseBeforeTargetEffects or
        SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted or
        SkillProgramTriggerWindow.CardUseCompleted or
        SkillProgramTriggerWindow.SlashTargetRedirecting or
        SkillProgramTriggerWindow.SlashBeforeResponse or
        SkillProgramTriggerWindow.SlashFullyDodged =>
            Common | ProgramContextCapability.CardAction | ProgramContextCapability.Judgment,
        _ when SupportsWindow(window.Value) => Common,
        _ => throw new InvalidOperationException($"Window '{window}' has no shared program-frame adapter.")
    };
}
