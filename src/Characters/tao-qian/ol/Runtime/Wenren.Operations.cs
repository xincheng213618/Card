using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record WenrenCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int RecipientCount) : IGameEvent;
public sealed record WenrenRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record WenrenRecipientEvaluatedEvent(long FrameId, int Index, int RecipientSeat, int OwnerHand, int RecipientHand, int Amount) : IGameEvent;
public sealed record WenrenDrawReturnedEvent(long FrameId, int Index, int RecipientSeat, int RequestedCount, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;
internal sealed class ResolveWenrenDescriptor() : TaoQianDescriptor(SkillProgramEffectOp.ResolveWenrenSequentialDraw){
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
