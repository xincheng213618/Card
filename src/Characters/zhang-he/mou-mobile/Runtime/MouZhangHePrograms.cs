using System.Text.Json.Serialization;
namespace CardGame.Core;

// This native boundary records a skip that has actually occurred, never a
// requested skip or a fictitious phase-start/phase-end.
public sealed record ProgramActualPhaseSkipContext(int OwnerSeat, int ActualTurn, SkillProgramTurnPhase Phase,
    int DelayedEffects, bool AfterNormalDrawProgramsCompleted = false, bool DrawPhaseEndedProgramsCompleted = false,
    bool ActualDrawCompletionCompleted = false);
public sealed record MouZhangHeSkipEntitlement(long PlanFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, SkillProgramTurnPhase Phase);
public sealed record MouZhangHeSkipBoundary(MouZhangHeSkipEntitlement Entitlement, ProgramActualPhaseSkipContext NativeReturn);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouZhangHeSkipBoundary? MouZhangHeSkip { get; init; }
}
public enum MouZhangHeStage { CostCards, Phases, PayCost, CostChildren, ObtainTargets, ObtainCard, ObtainChildren,
    FieldCard, FieldTarget, FieldChildren, Beneficiary, Drawing, DrawChildren, ExtraPlay, Complete }
public sealed record MouZhangHeCard(int CardId, CardLocation From);
public sealed record MouZhangHeExtraPlayReturn(TurnEndingBoundaryFrame Parent, TurnPhase CoarsePhase,
    ActualDiscardRecoveryPhaseKey? ActualPhase, ProgramPhaseSchedule? SuspendedSchedule);
public sealed record MouZhangHeReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, long WindowId)
{
    public MouZhangHeStage Stage { get; init; }
    private readonly IReadOnlyList<MouZhangHeCard> _cards=[];
    private readonly IReadOnlyList<SkillProgramTurnPhase> _phases=[];
    private readonly IReadOnlyList<int> _targets=[];
    public IReadOnlyList<MouZhangHeCard> Cards { get=>_cards; init=>_cards=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<SkillProgramTurnPhase> Phases { get=>_phases; init=>_phases=Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Targets { get=>_targets; init=>_targets=Array.AsReadOnly(value.ToArray()); }
    public int Cursor { get; init; }
    public int PaidCount { get; init; }
    public bool Accepted { get; init; }
    public int CardId { get; init; }
    public CardLocation? From { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int OwnerCountBefore { get; init; }
    public int TargetFieldBefore { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public MouZhangHeSkipEntitlement? Entitlement { get; init; }
    public MouZhangHeExtraPlayReturn? ExtraReturn { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public MouZhangHeReceipt? MouZhangHe { get; init; }
}
public sealed record MouZhangHeStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    SkillProgramEffectOp Operation, int ActualTurn, long WindowId) : IGameEvent;
public sealed record MouZhangHeCostPaidEvent(long FrameId, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record MouZhangHeSkipIssuedEvent(MouZhangHeSkipEntitlement Entitlement) : IGameEvent;
public sealed record MouZhangHeSkipObservedEvent(long WindowId, MouZhangHeSkipEntitlement Entitlement,
    ProgramActualPhaseSkipContext NativeReturn) : IGameEvent;
public sealed record MouZhangHeSkipReturnedEvent(long WindowId, long PlanFrameId, SkillProgramTurnPhase Phase) : IGameEvent;
// Only the movement sequence is public; the acquired hidden Hand identity is
// read through the trusted native movement receipt, never exposed by this fact.
public sealed record MouZhangHeObtainedEvent(long FrameId, int OriginalRecipientSeat, long MovementSequence) : IGameEvent;
public sealed record MouZhangHeDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual,
    int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouZhangHeFieldMovedEvent(long FrameId, int CardId, int FromSeat, int ToSeat, long Sequence) : IGameEvent;
public sealed record MouZhangHeLiaoshiUsedEvent(int OwnerSeat, int ActualTurn, long ActionId, int CardId, CardKind OutputKind) : IGameEvent;
public sealed record MouZhangHeEndingConsumedEvent(long FrameId, long PlanFrameId, int OwnerSeat, int ActualTurn) : IGameEvent;
public sealed record MouZhangHeCompletedEvent(long FrameId) : IGameEvent;
internal interface IMouZhangHeHost { SkillProgramStepOutcome Start(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouZhangHeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler=>new MouZhangHeHandler(Op);
    public override ProgramOperationInteraction Interaction=>ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy {get;}=new(ProgramOperationAiSemantic.GainCards,static(_,c)=>c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements=>new(Triggered:ProgramTrackedFact.NativeLogicalDiscard|ProgramTrackedFact.ActualDiscardRecoveryPhase|ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(Op switch
    {
        SkillProgramEffectOp.PrepareMouZhangHeQiaobian=>SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
        SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase=>SkillProgramTriggerWindow.ActualPhaseSkipped,
        _=>SkillProgramTriggerWindow.TurnEnding
    })];
}
internal sealed class PrepareMouZhangHeQiaobianDescriptor:MouZhangHeDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.PrepareMouZhangHeQiaobian;}
internal sealed class ResolveMouZhangHeSkippedPhaseDescriptor:MouZhangHeDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouZhangHeSkippedPhase;}
internal sealed class ResolveMouZhangHeEndingDescriptor:MouZhangHeDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMouZhangHeEnding;}
internal sealed class MouZhangHeHandler(SkillProgramEffectOp operation):ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect,ProgramSkillFrame frame,int target,ISkillProgramEffectHost host)=>host.GetCapability<IMouZhangHeHost>().Start(frame,operation);
}
