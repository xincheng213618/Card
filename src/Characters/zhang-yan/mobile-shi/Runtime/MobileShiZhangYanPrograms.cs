using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiZyStage { Path, Commit, RevealChildren, DiscardChildren, Color, RecoverChildren, ObtainChildren, DuelIssued, DuelChildren, Finished }
public sealed record ShiZyHandPlan(int Seat)
{
    private IReadOnlyList<int> _hand = [];
    public IReadOnlyList<int> Hand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public int Selected { get; init; }
    public bool Committed { get; init; }
    public Suit Suit { get; init; }
    public long DiscardSequence { get; init; }
}
public sealed record ShiZyReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, long WindowId, long UseId, long ActionId, int Target, ShiZyStage Stage)
{
    private IReadOnlyList<ShiZyHandPlan> _plans = [];
    public IReadOnlyList<ShiZyHandPlan> Plans { get => _plans; init => _plans = Array.AsReadOnly(value.Select(p => p with { Hand = p.Hand }).ToArray()); }
    public int Direction { get; init; }
    public int Cursor { get; init; }
    public int CardId { get; init; }
    public long PaymentSequence { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long NativeUseId { get; init; }
    public ProgramVirtualOrdinaryTrickOrigin? Duel { get; init; }
    public bool? OriginalAttackActive { get; init; }
    public int? OriginalTargetIndex { get; init; }
}
public sealed record ShiZySlashPlan(long ProgramFrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, int OriginalActor, int OriginalTarget)
{
    private IReadOnlyList<ShiZyHandPlan> _payments = [];
    private IReadOnlyList<int> _extra = [];
    public IReadOnlyList<ShiZyHandPlan> Payments { get => _payments; init => _payments = Array.AsReadOnly(value.Select(p => p with { Hand = [] }).ToArray()); }
    public IReadOnlyList<int> Extra { get => _extra; init => _extra = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiZyReceipt? ShiZhangYan { get; init; } }
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiZySlashPlan? ShiZhangYanPath { get; init; }
}
public sealed record ShiZyStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, int Turn, long WindowId, long UseId, long ActionId, int Target,
    bool? OriginalAttackActive = null, int? OriginalTargetIndex = null) : IGameEvent;
public sealed record ShiZyPathSeatEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiZyHandCommittedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record ShiZyHandRevealedEvent(long FrameId, int Index, int Seat, int CardId, Suit Suit) : IGameEvent;
public sealed record ShiZyHandDiscardedEvent(long FrameId, int Index, int Seat, int CardId, long Sequence) : IGameEvent;
public sealed record ShiZyTargetAddedEvent(long FrameId, long UseId, long ActionId, int Target, int CardId, long Sequence) : IGameEvent;
public sealed record ShiZyPreventedEvent(long FrameId, long UseId, long ActionId, int Target, int CardId, long PaymentSequence) : IGameEvent;
public sealed record ShiZyCardObtainedEvent(long FrameId, int CardId, long Before, long After) : IGameEvent;
public sealed record ShiZyDuelIssuedEvent(long UseId, ProgramVirtualOrdinaryTrickOrigin Origin) : IGameEvent;
public sealed record ShiZyDuelReturnedEvent(long UseId, ProgramVirtualOrdinaryTrickOrigin Origin) : IGameEvent;
public sealed record ShiZyAttackRestoredEvent(long FrameId, long UseId, long ActionId, int TargetIndex, bool Active) : IGameEvent;

internal interface IShiZhangYanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShiZhangYanDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler => new ShiZhangYanHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op == SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing ? SkillProgramTriggerWindow.CardUseTargetsFinalized :
        op == SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent ? SkillProgramTriggerWindow.BeforeDamageApplied : SkillProgramTriggerWindow.CardUseCompleted)];
}
internal sealed class ResolveMobileShiZhangYanFeijingDescriptor() : ShiZhangYanDescriptor(SkillProgramEffectOp.ResolveMobileShiZhangYanFeijing);
internal sealed class ResolveMobileShiZhangYanXiaogePreventDescriptor() : ShiZhangYanDescriptor(SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogePrevent);
internal sealed class ResolveMobileShiZhangYanXiaogeDuelDescriptor() : ShiZhangYanDescriptor(SkillProgramEffectOp.ResolveMobileShiZhangYanXiaogeDuel);
internal sealed class ShiZhangYanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IShiZhangYanHost>().Resolve(f, op);
}
