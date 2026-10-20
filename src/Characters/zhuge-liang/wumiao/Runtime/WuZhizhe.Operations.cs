using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WuZhugeLiangCopyCreatedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int OriginalCardId, int CardId, CardKind Kind, Suit Suit, int Rank) : IGameEvent;
public sealed record WuZhugeLiangCopyReturnedEvent(int OwnerSeat, int CardId, long EntrySequence, long ReturnSequence, CardMoveReason Reason, int ActualTurn, int TurnOwner) : IGameEvent;
internal sealed class ResolveWuZhugeLiangZhizheDescriptor : WuZhugeLiangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuZhugeLiangZhizhe;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
