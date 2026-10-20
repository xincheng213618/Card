using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record LeXidiOpeningEntity(int OwnerSeat, int CardId, long EntrySequence);
public sealed partial record ProgramLifecycleTriggerWindowFrame
{
    private readonly IReadOnlyList<LeXidiOpeningEntity>? _leXidiOpening;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<LeXidiOpeningEntity>? LeXidiOpening
    { get => _leXidiOpening; init => _leXidiOpening = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record LeXidiEntityMarkedEvent(long FrameId, CardConversionSource Source, int CardId, long EntrySequence, bool Initial) : IGameEvent;
public sealed record LeXidiInitializedEvent(long FrameId, CardConversionSource Source, long WindowId, int Count) : IGameEvent;
public sealed record LeXidiReorderStartedEvent(long FrameId, int OwnerSeat, int FluteCount, int RequestedCount) : IGameEvent;
public enum LeChengyanStage { FirstDraw, RevealChildren, SecondDraw, SecondDrawChildren }
public sealed record LeChengyanReceipt(CardConversionSource Source, string GameplayHash, long UseId, long ActionId)
{
    public LeChengyanStage Stage { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int? RevealedCardId { get; init; }
    public CardKind? RevealedKind { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeChengyanReceipt? LeChengyan { get; init; } }
public sealed record LeChengyanRevealedEvent(long FrameId, long UseId, long ActionId, int CardId, CardKind EffectiveKind) : IGameEvent;
public sealed record LeChengyanDrawReturnedEvent(long FrameId, bool Second, int ActualCount, int ReplacementCount, long? AttemptFrameId) : IGameEvent;
public sealed record LeChengyanEffectIssuedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, int ShownCardId, CardKind EffectKind) : IGameEvent;
public sealed record LeChengyanCompletedEvent(long FrameId, long UseId, bool Replaced) : IGameEvent;

internal interface ILeZhugeGuoHost
{
    SkillProgramStepOutcome Initialize(ProgramSkillFrame f);
    SkillProgramStepOutcome Reorder(ProgramSkillFrame f);
    SkillProgramStepOutcome Chengyan(ProgramSkillFrame f);
}
internal abstract class LeZhugeGuoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ISkillProgramEffectHandler Handler => new LeZhugeGuoHandler(Op);
}
internal sealed class InitializeLeZhugeGuoXidiDescriptor : LeZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeLeZhugeGuoXidi;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)];
}
internal sealed class ReorderLeZhugeGuoXidiDescriptor : LeZhugeGuoDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReorderLeZhugeGuoXidi; }
internal sealed class ResolveLeZhugeGuoChengyanDescriptor : LeZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeZhugeGuoChengyan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class LeZhugeGuoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => op switch
    {
        SkillProgramEffectOp.InitializeLeZhugeGuoXidi => h.GetCapability<ILeZhugeGuoHost>().Initialize(f),
        SkillProgramEffectOp.ReorderLeZhugeGuoXidi => h.GetCapability<ILeZhugeGuoHost>().Reorder(f),
        _ => h.GetCapability<ILeZhugeGuoHost>().Chengyan(f)
    };
}
