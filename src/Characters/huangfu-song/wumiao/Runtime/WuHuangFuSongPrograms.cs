using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WuHuangFuSongStage { Choosing, ChoosingMinimumHand, MovementChildren, Recovering, Drawing, DrawChildren, LossChildren }
public sealed record WuHuangFuSongTurn(int Number, int OwnerSeat);
public sealed record WuHuangFuSongReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    WuHuangFuSongTurn Turn, long WindowId, long UseId)
{
    public WuHuangFuSongStage Stage { get; init; }
    public int? TargetSeat { get; init; }
    public int? CardId { get; init; }
    public CardLocation? From { get; init; }
    public CardLocation? To { get; init; }
    public int Rank { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int RequestedDraw { get; init; }
}
public sealed record WuHuangFuSongUseReceipt(long UseId, long ActionId, int ActorSeat, int ProviderSeat,
    CardKind Kind, int? EffectiveRank, int MaterialCount, bool HandUse, bool LianjieMaterials, WuHuangFuSongTurn Turn);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuHuangFuSongReceipt? WuHuangFuSong { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuHuangFuSongUseReceipt? WuHuangFuSongUse { get; init; } }
public sealed record WuHuangFuSongStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, WuHuangFuSongTurn Turn, long WindowId, long UseId) : IGameEvent;
public sealed record WuHuangFuSongMovedEvent(long FrameId, int CardId, int Rank, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WuHuangFuSongMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record WuHuangFuSongBottomPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, int CardId, int Rank, long Sequence, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement, long Before, long After) : IGameEvent;
public sealed record WuHuangFuSongUseAdmittedEvent(WuHuangFuSongUseReceipt Receipt) : IGameEvent;
public sealed record WuHuangFuSongCompletedEvent(long FrameId, SkillProgramEffectOp Operation, bool Paid) : IGameEvent;
internal interface IWuHuangFuSongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class WuHuangFuSongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(8));
}
internal abstract class WuHuangFuSongHandler : ISkillProgramEffectHandler
{ public abstract SkillProgramEffectOp Op { get; } public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IWuHuangFuSongHost>().Resolve(f, Op); }
