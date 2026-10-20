using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouXuHuangAttack { Siege, Advance }
public enum MouXuHuangDefense { BlockSiege, BlockAdvance }
public enum MouXuHuangDuanliangStage { AttackChoice, DefenseChoice, Revealed, DuelTargets, RefillChildren, ObtainChoice, ObtainChildren, NativeIssuing, NativeUse, NativeChildren }
public sealed record MouXuHuangNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int ActorSeat, int TargetSeat, CardKind Kind, int CardId,
    CardKind PrintedKind, CardLocation? From, Suit Suit, int Rank, bool? EffectiveIsRed);
public sealed record MouXuHuangGiftReceipt(int Cursor, int GiverSeat, int CardId, CardLocation From, CardLocation To, long Sequence);
public sealed record MouXuHuangDuanliangReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int TargetSeat)
{
    public MouXuHuangDuanliangStage Stage { get; init; }
    public MouXuHuangAttack? Attack { get; init; }
    public MouXuHuangDefense? Defense { get; init; }
    public bool Revealed { get; init; }
    public bool Success { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public MouXuHuangNativeOrigin? Native { get; init; }
    public long? PaymentSequence { get; init; }
    public bool NativeReturned { get; init; }
    public bool AttackReturned { get; init; }
    public MouXuHuangGiftReceipt? Obtained { get; init; }
    private readonly IReadOnlyList<int> _nativeTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> NativeTargets { get => _nativeTargets; init => _nativeTargets = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatchIds { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXuHuangDuanliangReceipt? MouXuHuangDuanliang { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXuHuangShipoReceipt? MouXuHuangShipo { get; init; }
}
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXuHuangNativeOrigin? MouXuHuangNative { get; init; } }
public sealed record MouXuHuangMouyiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int TargetSeat) : IGameEvent;
public sealed record MouXuHuangStrategyCommittedEvent(long FrameId, int ParticipantSeat, bool Attacker) : IGameEvent;
public sealed record MouXuHuangMouyiRevealedEvent(long FrameId, MouXuHuangAttack Attack, MouXuHuangDefense Defense, bool Success) : IGameEvent;
public sealed record MouXuHuangNativeIssuedEvent(MouXuHuangNativeOrigin Origin) : IGameEvent;
public sealed record MouXuHuangDuelTargetSelectedEvent(long FrameId, int Index, int TargetSeat, bool Adjusted) : IGameEvent;
public sealed record MouXuHuangTopPaidEvent(long FrameId, long UseId, int CardId, long Sequence) : IGameEvent;
public sealed record MouXuHuangNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouXuHuangNativeAttackReturnedEvent(long FrameId, long UseId) : IGameEvent;
public sealed record MouXuHuangNativeBatchSignedEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
public sealed record MouXuHuangMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouXuHuangObtainPaidEvent(long FrameId, int TargetSeat, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouXuHuangDuanliangCompletedEvent(long FrameId, bool Success, bool UsedCard, bool ObtainedCard) : IGameEvent;

internal interface IMouXuHuangHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouXuHuangDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(8));
    public override ISkillProgramEffectHandler Handler => new MouXuHuangHandler(Op);
}
internal sealed class MouXuHuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouXuHuangHost>().Resolve(f, op);
}
