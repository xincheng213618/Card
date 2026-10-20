using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record LianjuEntityUsedEvent(int ActualTurn, int ActorSeat, long? ActionId, long UseFrameId, int CardId) : IGameEvent;
public sealed record LianjuUseActorChangedEvent(long UseFrameId, long ActionId, long ProducerFrameId, CardConversionSource Producer,
    string GameplayHash, int InstructionIndex, int PreviousActor, int Actor, int Provider) : IGameEvent;
public sealed record LianjuEndingReachedEvent(int Serial, int ActualTurn, int ActorSeat, long NativePhaseToken) : IGameEvent;
public sealed record LianjuPromiseGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EndingSerial,
    int ActualTurn, int RecipientSeat, bool IsRed) : IGameEvent;
public sealed record LianjuPromiseConsumedEvent(long PromiseFrameId, long ParentFrameId, int EndingSerial) : IGameEvent;
internal sealed class OfferLianjuSameColorGiftDescriptor : LiWanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferLianjuSameColorGift; }
internal sealed class ResolveLianjuOppositeColorReturnDescriptor : LiWanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLianjuOppositeColorReturn; }
