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
public sealed record WuHuangFuSongChaozhenDisabledEvent(long FrameId, int OwnerSeat, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongBottomPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, int CardId, int Rank, long Sequence, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement, long Before, long After) : IGameEvent;
public sealed record WuHuangFuSongLianjieEntityEvent(long FrameId, int OwnerSeat, int CardId, long DrawSequence, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongUseAdmittedEvent(WuHuangFuSongUseReceipt Receipt) : IGameEvent;
public sealed record WuHuangFuSongJiangxianArmedEvent(long FrameId, CardConversionSource Source, string GameplayHash, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongJiangxianSettledEvent(long FrameId, int OwnerSeat, string? LostSkill, WuHuangFuSongTurn Turn) : IGameEvent;
public sealed record WuHuangFuSongSkillRemovedEvent(long FrameId, int OwnerSeat, string SkillId, string GrantId) : IGameEvent;
public sealed record WuHuangFuSongCompletedEvent(long FrameId, SkillProgramEffectOp Operation, bool Paid) : IGameEvent;
internal interface IWuHuangFuSongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class WuHuangFuSongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(8));
}
internal sealed class ResolveWuHuangFuSongChaozhenDescriptor : WuHuangFuSongDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongChaozhenHandler(); }
internal sealed class ResolveWuHuangFuSongLianjieDescriptor : WuHuangFuSongDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongLianjie; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongLianjieHandler(); }
internal sealed class ArmWuHuangFuSongJiangxianDescriptor : WuHuangFuSongDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWuHuangFuSongJiangxian; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongArmHandler(); }
internal sealed class SettleWuHuangFuSongJiangxianDescriptor : WuHuangFuSongDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleWuHuangFuSongJiangxian; public override ISkillProgramEffectHandler Handler { get; } = new WuHuangFuSongSettleHandler(); }
internal abstract class WuHuangFuSongHandler : ISkillProgramEffectHandler
{ public abstract SkillProgramEffectOp Op { get; } public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IWuHuangFuSongHost>().Resolve(f, Op); }
internal sealed class WuHuangFuSongChaozhenHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen; }
internal sealed class WuHuangFuSongLianjieHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuHuangFuSongLianjie; }
internal sealed class WuHuangFuSongArmHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmWuHuangFuSongJiangxian; }
internal sealed class WuHuangFuSongSettleHandler : WuHuangFuSongHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleWuHuangFuSongJiangxian; }
