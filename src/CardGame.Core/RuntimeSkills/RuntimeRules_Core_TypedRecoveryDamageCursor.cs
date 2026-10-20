namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TypedRecoveryDamageCursor
{
    internal static bool TypedPaidDyingEntryReturnMatches(DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry) =>
        entry.Window == SkillProgramTriggerWindow.DyingEntering && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
        entry.ResumeDyingFrameId == dying.Id && entry.OwnerSeat == dying.VictimSeat &&
        entry.ResumeProgramFrameId is null && entry.ResumeDrawPhaseObligationFrameId is null && entry.ProgramTarget is null &&
        entry.CharacterStateContinuation is null && entry.ResumeCardId is null && entry.ResumeCardKind is null &&
        entry.ParticipantFacts is { } facts && facts.GetValueOrDefault(dying.VictimSeat) == entry.Facts &&
        entry.CandidateIndex >= 0 && entry.CandidateIndex <= entry.Candidates.Count &&
        dying.AttemptedSelfDyingBindings.Contains("window:dying-entering");

    internal static bool IsTypedRecoveryTargetSupported(RecoverProgramInstruction instruction) => instruction.Target is
        SkillProgramEffectTarget.Owner or SkillProgramEffectTarget.Actor or SkillProgramEffectTarget.SelectedTarget or SkillProgramEffectTarget.SelectedTargets;

    internal static bool IsRecoveryCursorPrimitive(ProgramSkillInstruction? instruction) => instruction is
        DrawProgramInstruction or RecoverProgramInstruction or LoseHpProgramInstruction or DamageProgramInstruction or
        ChangeMaximumHpProgramInstruction or GrowMaximumHpAndHpProgramInstruction;
}
