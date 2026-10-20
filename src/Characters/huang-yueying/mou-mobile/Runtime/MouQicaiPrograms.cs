using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record MouHuangYueyingQicaiReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int RecipientSeat) : IGameEvent;
public sealed record MouHuangYueyingQicaiEquipmentPlacedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId, CardLocation From, CardLocation To, long MovementSequence, int? ReplacedCardId) : IGameEvent;
public sealed record MouHuangYueyingQiGrantedEvent(long GrantId, CardConversionSource Source, string GameplayHash, int RecipientSeat, long MovementSequence, long? PreviousGrantId) : IGameEvent;
public sealed record MouHuangYueyingQiGainClaimedEvent(long GrantId, long BatchId, int MovementIndex, CardConversionSource Source, string GameplayHash, int GainingSeat, int CardId, CardLocation OriginalFrom, long GainSequence, int Remaining) : IGameEvent;
public sealed record MouHuangYueyingQiTransferredEvent(long FrameId, long GrantId, long GainSequence, int CardId, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record MouHuangYueyingQiClaimSettledEvent(long FrameId, long GrantId, long GainSequence) : IGameEvent;
internal sealed class PlaceMouHuangYueyingQicaiEquipmentDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceMouHuangYueyingQicaiEquipment;
  public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase); }
internal sealed class TransferMouHuangYueyingQicaiGainsDescriptor : MouHuangYueyingMobileDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains; }
