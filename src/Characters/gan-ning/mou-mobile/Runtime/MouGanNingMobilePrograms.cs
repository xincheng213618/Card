using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouGanNingStage { Guess, Repeat, Discard, PlaceCard, PlaceTarget, WeiChoice, MovementChildren, Drawing, DrawChildren }
public sealed record MouGanNingReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    ActualDiscardRecoveryPhaseKey? Phase, int TargetSeat, long UseId, long ActionId)
{
    public MouGanNingStage Stage { get; init; }
    public int GuessedMask { get; init; }
    public int Wrong { get; init; }
    public int Discarded { get; init; }
    public int Deposited { get; init; }
    public int SelectedCard { get; init; }
    public int TargetIndex { get; init; }
    private IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public string? MovePurpose { get; init; }
    public bool Accepted { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGanNingReceipt? MouGanNing { get; init; } }
public sealed record MouGanNingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, ActualDiscardRecoveryPhaseKey? Phase, int TargetSeat, long UseId, long ActionId) : IGameEvent;
public sealed record MouGanNingGuessedEvent(long FrameId, int ChooserSeat, Suit Suit, bool Correct, int Wrong, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouGanNingRepeatEvent(long FrameId, bool Continue) : IGameEvent;
public sealed record MouGanNingDiscardedEvent(long FrameId, int Index, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouGanNingWeiPlacedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int RecipientSeat, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouGanNingWeiChosenEvent(long FrameId, long UseId, int TargetSeat, int CardId, string Branch) : IGameEvent;
public sealed record MouGanNingWeiSpentEvent(long FrameId, long UseId, int TargetSeat, int CardId, string Branch, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouGanNingTargetCancelledEvent(long FrameId, long UseId, long ActionId, int TargetSeat, int CardId) : IGameEvent;
public sealed record MouGanNingMovementEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouGanNingDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouGanNingCompletedEvent(long FrameId, int Wrong, int Discarded, int Deposited) : IGameEvent;

internal interface IMouGanNingHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouGanNingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ISkillProgramEffectHandler Handler => new MouGanNingHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ResolveMouGanNingQixiDescriptor : MouGanNingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGanNingQixi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class ResolveMouGanNingFenweiDescriptor : MouGanNingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGanNingFenwei;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class ResolveMouGanNingWeiTargetDescriptor : MouGanNingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGanNingWeiTarget;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseBeforeTargetEffects)];
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class MouGanNingHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouGanNingHost>().Resolve(f, op);
}
