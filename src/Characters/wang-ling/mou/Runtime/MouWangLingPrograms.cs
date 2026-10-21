using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouWangLingStage { GiftCards, GiftRecipient, GiftChildren, Drawing, DrawChildren, Retaining, DiscardChildren, Awakening }
public sealed record MouWangLingMaterial(int CardId, CardKind Name);
public sealed record MouWangLingGift(int CardId, CardKind Name, int RecipientSeat);
public sealed record MouWangLingReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    int ActualTurn, long ParentFrameId)
{
    public MouWangLingStage Stage { get; init; }
    public bool Paid { get; init; }
    public int SelectedCard { get; init; }
    public int Cursor { get; init; }
    public int Requested { get; init; }
    public int FirstNames { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<MouWangLingMaterial> _materials = [];
    private IReadOnlyList<MouWangLingGift> _gifts = [];
    private IReadOnlyList<int> _kept = [];
    public IReadOnlyList<MouWangLingMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouWangLingGift> Gifts { get => _gifts; init => _gifts = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Kept { get => _kept; init => _kept = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouWangLingReceipt? MouWangLing { get; init; } }
public sealed record MouWangLingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int ActualTurn, long ParentFrameId) : IGameEvent;
public sealed record MouWangLingMaterialEvent(long FrameId, int Index, int CardId, CardKind Name) : IGameEvent;
public sealed record MouWangLingGiftAcceptedEvent(long FrameId, int Count) : IGameEvent;
public sealed record MouWangLingGiftEvent(long FrameId, int OwnerSeat, int Index, int CardId, CardKind Name, int RecipientSeat, long Sequence, bool FirstName) : IGameEvent;
public sealed record MouWangLingDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouWangLingKeptEvent(long FrameId, int CardId, CardKind Name) : IGameEvent;
public sealed record MouWangLingDiscardedEvent(long FrameId, int CardId, CardKind Name, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouWangLingAwakeningAcceptedEvent(long FrameId, int OwnerSeat, int Names) : IGameEvent;
public sealed record MouWangLingCompletedEvent(long FrameId, SkillProgramEffectOp Operation, int FirstNames) : IGameEvent;

internal interface IMouWangLingRuntime : IProgramRuntimeOperations
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouWangLingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouWangLingHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeTrueUseProcessingEntry | ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch {
        SkillProgramEffectOp.ResolveMouWangLingJichou => SkillProgramTriggerWindow.TurnEnding,
        SkillProgramEffectOp.PrepareMouWangLingMouli => SkillProgramTriggerWindow.AfterTurnEnded,
        _ => SkillProgramTriggerWindow.PlayPhaseStarting })];
}
internal sealed class ResolveMouWangLingJichouDescriptor : MouWangLingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouWangLingJichou; }
internal sealed class PrepareMouWangLingMouliDescriptor : MouWangLingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareMouWangLingMouli; }
internal sealed class ResolveMouWangLingZifuDescriptor : MouWangLingDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouWangLingZifu; }
internal sealed class MouWangLingHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouWangLingRuntime>().Resolve(frame, operation);
}
