namespace CardGame.Core;

[Flags]
internal enum ProgramTrackedFact
{
    None = 0,
    NativeLogicalDiscard = 1,
    ActualDiscardRecoveryPhase = 2,
    NativeTrueUseProcessingEntry = 4,
    WholeActualUseDeclaration = 8
}

// Definition-only demand for shared native facts. Keep trigger and activation
// sources separate: using an operation at another entry point must not enable
// facts that the historical catalog predicate did not request.
internal readonly record struct ProgramFactRequirements(
    ProgramTrackedFact Triggered = ProgramTrackedFact.None,
    ProgramTrackedFact Activated = ProgramTrackedFact.None)
{
    internal ProgramFactRequirements Union(ProgramFactRequirements other) =>
        new(Triggered | other.Triggered, Activated | other.Activated);

    internal bool IsValid => ((Triggered | Activated) &
        ~(ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase |
          ProgramTrackedFact.NativeTrueUseProcessingEntry | ProgramTrackedFact.WholeActualUseDeclaration)) == ProgramTrackedFact.None;
}
