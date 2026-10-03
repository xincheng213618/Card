namespace CardGame.Core;

// Scalar, owned by the paid prevention program. Physical movement identity is
// proved against the immutable ledger interval, never a parallel pending object.
public sealed record ProgramPreventionDrawReceipt(int InstructionIndex, long BeforeDamageFrameId,
    int SourceSeat, int TargetSeat, int PreventedAmount, int Multiplier,
    long MovementSequenceBefore, long MovementSequenceAfter, int ActualDrawCount);
