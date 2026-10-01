namespace CardGame.Core;

/// <summary>
/// The selected physical cards paid by one already-advanced program instruction.
/// This belongs to the program frame so a paused movement window cannot pay it again.
/// </summary>
public sealed record ProgramSelectedCardPayment(
    int InstructionIndex,
    SkillProgramEffectOp Operation,
    IReadOnlyList<int> CardIds,
    int RecipientSeat,
    bool MovementCommitted = false,
    long? ActiveChildFrameId = null,
    long? LastCompletedChildFrameId = null);

/// <summary>The result returned to the parent after its movement windows finish.</summary>
public sealed record ProgramSelectedCardPaymentResult(
    int InstructionIndex,
    SkillProgramEffectOp Operation,
    long? LastChildFrameId,
    bool Completed);
