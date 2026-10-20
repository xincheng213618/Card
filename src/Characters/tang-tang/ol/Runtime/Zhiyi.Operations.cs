using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ZhiyiOwnedOriginEvent(long BatchId, long Sequence, int CardId, int OwnerSeat, CardLocation From, CardLocation To, Suit Suit) : IGameEvent;
public sealed record ZhiyiTransferProducerEvent(long EntrySequence, int RecipientSeat, long ProducerId) : IGameEvent;
public sealed record ZhiyiGainFrozenEvent(TangTangGain Gain) : IGameEvent;
public sealed record ZhiyiOpportunityPaidEvent(long FrameId, int OwnerSeat, int ActualTurn, int TurnOwner, long BatchId) : IGameEvent;
public sealed record ZhiyiRecipientChosenEvent(long FrameId, int RecipientSeat) : IGameEvent;
internal sealed class ResolveZhiyiGainColorBenefitDescriptor() : TangTangDescriptor(SkillProgramEffectOp.ResolveZhiyiGainColorBenefit) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.TangTang is { Paid: true };
}
