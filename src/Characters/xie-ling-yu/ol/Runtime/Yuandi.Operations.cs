using System.Text.Json.Serialization;

namespace CardGame.Core;

// History carries scalar proofs; all private selections remain on the owning program.
public sealed record YuandiDiscardEnteredEvent(long BatchId, int ActualTurn, int TurnOwner, long MovementSequence,
    int CardId, CardKind PrintedKind, CardKind Name, bool FromTrueUse) : IGameEvent;
public sealed record YuandiOpportunityEvent(long BatchId, int OwnerSeat, string SkillInstanceId, string GameplayHash,
    int ActualTurn, int TurnOwner, long CompletionSequence, int NamesMask, bool NonUse, int OccurrenceIndex) : IGameEvent;
public sealed record YuandiPaidEvent(long FrameId, int OwnerSeat, long BatchId, int ActualTurn, int TurnOwner,
    long CompletionSequence, int NamesMask, bool NonUse) : IGameEvent;
public sealed record YuandiNameRemovedEvent(long FrameId, int OwnerSeat, CardKind Name, int BeforeMask, int AfterMask) : IGameEvent;
internal sealed class ResolveYuandiDiscardNamesDescriptor() : XieLingYuDescriptor(SkillProgramEffectOp.ResolveYuandiDiscardNames) {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.XieLingYu is { Paid: true } || context.State.CanContinueAfterOwnerDeath(context.Frame);
}
