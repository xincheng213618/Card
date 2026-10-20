using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouLuZhiOlStage { Red, RedChildren, TargetRed, TargetRedChildren, Black, BlackChildren, SlashTarget, Issuing, NativeUse, NativeReturned, Drawing, DrawChildren }
public sealed record MouLuZhiOlReceipt(CardConversionSource Source, string GameplayHash, long WindowId, long OriginalId, long OriginalActionId, int ActorSeat, int TargetSeat, int Turn, SkillProgramEffectOp Op)
{
    public MouLuZhiOlStage Stage { get; init; }
    public bool Paid { get; init; }
    public int Required { get; init; }
    public int Requested { get; init; }
    public int CounterTarget { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public bool OuterWasActive { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public long BatchId { get; init; }
    public int OriginalInstruction { get; init; }
    private IReadOnlyList<int> _selected = [];
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLuZhiOlCounterOrigin(long FrameId, CardConversionSource Source, string GameplayHash, long OriginalId, long OriginalActionId, long UseId, long ActionId, int ActorSeat, int TargetSeat);
public sealed record MouLuZhiOlResponseBan(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int TargetSeat, int Instruction = 0);
public sealed record MouLuZhiOlDirectDuel(long FrameId, int Instruction, CardConversionSource Producer, string Hash, int Actor, int Target)
{
    public bool PressClosed { get; init; }
    public bool Returned { get; init; }
    public bool CounterClosed { get; init; }
    public bool DamageApplied { get; init; }
    public int FinalTarget { get; init; }
}
public sealed record MouLuZhiOlDirectWindowFrame(long Id, long ParentFrameId, bool Completed, ProgramTriggerCandidate Candidate, ProgramSkillWindowContext Context)
    : ResolutionFrame(Id, ResolutionFrameKind.MouLuZhiOlDirectWindow, ResolutionFrameStep.ResolvingEffect), IProgramChildResolutionFrame;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuZhiOlReceipt? MouLuZhiOl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuZhiOlDirectDuel? MouLuZhiOlDirectDuel { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuZhiOlCounterOrigin? MouLuZhiOlCounter { get; init; }
    private IReadOnlyList<MouLuZhiOlResponseBan>? _mouLuZhiOlBans;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<MouLuZhiOlResponseBan>? MouLuZhiOlBans { get => _mouLuZhiOlBans; init => _mouLuZhiOlBans = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouLuZhiOlStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long OriginalId, long ActionId, int Actor, int Target, int Turn, long BatchId) : IGameEvent;
public sealed record MouLuZhiOlPressUsedEvent(long FrameId, int Owner, int Turn, int Required) : IGameEvent;
public sealed record MouLuZhiOlDiscardedEvent(long FrameId, int Chooser, string Purpose, int Before, int After, int Actual) : IGameEvent;
public sealed record MouLuZhiOlResponseBannedEvent(MouLuZhiOlResponseBan Ban) : IGameEvent;
public sealed record MouLuZhiOlUseDamageEvent(long OriginalId, int Instruction, long DamageId, int Target, int Amount) : IGameEvent;
public sealed record MouLuZhiOlCounterIssuedEvent(MouLuZhiOlCounterOrigin Origin) : IGameEvent;
public sealed record MouLuZhiOlCounterReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouLuZhiOlDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouLuZhiOlLianceArmedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn) : IGameEvent;
public sealed record MouLuZhiOlLianceConsumedEvent(long FrameId, long AttackId, int Source, int Target, int Turn, bool Chain, int Cursor) : IGameEvent;
public sealed record MouLuZhiOlChainIssuedEvent(long AttackId, int Cursor, int Inherited) : IGameEvent;
public sealed record MouLuZhiOlChainRebasedEvent(long AttackId, int Cursor) : IGameEvent;
public sealed record MouLuZhiOlCompletedEvent(long FrameId, SkillProgramEffectOp Op) : IGameEvent;
public sealed record MouLuZhiOlDirectDeclaredEvent(MouLuZhiOlDirectDuel Duel) : IGameEvent;
public sealed record MouLuZhiOlDirectWindowEvent(long WindowId, long ProducerId, int Instruction, bool Completed, CardConversionSource Source, string Hash) : IGameEvent;
public sealed record MouLuZhiOlDirectResumedEvent(long WindowId, long ProducerId, bool Completed) : IGameEvent;
internal interface IMouLuZhiOlHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouLuZhiOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouLuZhiOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveMouLuZhiSibingPressDescriptor : MouLuZhiOlDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiSibingPress; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }
internal sealed class ResolveMouLuZhiSibingRetaliationDescriptor : MouLuZhiOlDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)]; }
internal sealed class ResolveMouLuZhiLianceDescriptor : MouLuZhiOlDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiLiance; public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger; public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardsMoved, SkillProgramTriggerWindow.CardsGained])]; }
internal sealed class MouLuZhiOlHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouLuZhiOlHost>().Resolve(f, op); }
