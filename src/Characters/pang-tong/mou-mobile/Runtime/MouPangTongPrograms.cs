using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouPangTongTargetsReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId)
{
    private readonly IReadOnlyList<int> _targets = Array.Empty<int>();
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouPangTongTargetsReceipt? MouPangTongTargets { get; init; } }
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? MouPangTongLateTargetSeat { get; init; } }
public sealed record MouPangTongConversionUsedEvent(CardConversionSource Source, ActualDiscardRecoveryPhaseKey Phase, long UseId, long ActionId) : IGameEvent;
public sealed record MouPangTongLianhuanPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId) : IGameEvent;
public sealed record MouPangTongUpgradedEvent(long FrameId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouPangTongTargetsStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, long UseId, long ActionId, int Count) : IGameEvent;
public sealed record MouPangTongTargetDiscardedEvent(long FrameId, int Index, int TargetSeat, int CardId, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouPangTongTargetVisitedEvent(long FrameId, int OwnerSeat, long UseId, long ActionId, int TargetSeat) : IGameEvent;
public sealed record MouPangTongLateTargetNotifiedEvent(long UseId, long ActionId, int TargetSeat, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouPangTongTargetsCompletedEvent(long FrameId, int OwnerSeat, long UseId, int TargetsVisited) : IGameEvent;
public sealed record MouPangTongMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IMouPangTongHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouPangTongDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ArmMouPangTongLianhuan => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireCardActionActor()],
        SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.SelfDyingResponse)]
    };
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler => new MouPangTongHandler(Op);
}
internal sealed class ArmMouPangTongLianhuanDescriptor : MouPangTongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmMouPangTongLianhuan; }
internal sealed class ResolveMouPangTongLianhuanTargetsDescriptor : MouPangTongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets; }
internal sealed class UpgradeMouPangTongLianhuanDescriptor : MouPangTongDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UpgradeMouPangTongLianhuan; }
internal sealed class MouPangTongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouPangTongHost>().Resolve(f, op);
}
