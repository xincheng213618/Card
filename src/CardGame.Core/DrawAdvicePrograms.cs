namespace CardGame.Core;

public enum DrawAdviceStage { Drawing, ChoosingDiscard, Discarding }

/// <summary>Paid draw and each forced HE discard stay on the original draw-end program.</summary>
public sealed record DrawAdviceReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    long ParentLifecycleFrameId, int ActualTurnNumber, int ActualTurnOwnerSeat, int RecipientSeat,
    int DrawActual, long DrawBefore, long DrawAfter)
{
    public DrawAdviceStage Stage { get; init; }
    public bool QualificationsFrozen { get; init; }
    public int OwnerHandCount { get; init; }
    public int OwnerMaxHp { get; init; }
    public int RecipientHandCount { get; init; }
    public int RecipientMaxHp { get; init; }
    public bool OwnerMustDiscard { get; init; }
    public bool RecipientMustDiscard { get; init; }
    public int ParticipantCursor { get; init; }
    public int RequiredDiscardCount { get; init; }
    private readonly IReadOnlyList<int> _selectedDiscardCardIds = Array.Empty<int>();
    private readonly IReadOnlyList<CardLocation> _selectedSourceLocations = Array.Empty<CardLocation>();
    public IReadOnlyList<int> SelectedDiscardCardIds
    { get => _selectedDiscardCardIds; init => _selectedDiscardCardIds = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> SelectedSourceLocations
    { get => _selectedSourceLocations; init => _selectedSourceLocations = Array.AsReadOnly(value.ToArray()); }
    public long LastDiscardBefore { get; init; }
    public long LastDiscardAfter { get; init; }
    public long LastDiscardBatchId { get; init; }
    public int OwnerDiscardActual { get; init; }
    public int RecipientDiscardActual { get; init; }
}

public sealed record DrawAdviceStartedEvent(long FrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, long ParentLifecycleFrameId, int ActualTurnNumber, int RecipientSeat,
    long SequenceBefore) : IGameEvent;
public sealed record DrawAdviceDrawIssuedEvent(long FrameId, int RecipientSeat, int ActualCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record DrawAdviceQualifiedEvent(long FrameId, int OwnerSeat, int RecipientSeat,
    int OwnerHandCount, int OwnerMaxHp, int RecipientHandCount, int RecipientMaxHp,
    bool OwnerMustDiscard, bool RecipientMustDiscard) : IGameEvent;
public sealed record DrawAdviceDiscardIssuedEvent(long FrameId, int DiscardSeat, int ActualCount,
    long BatchId, long SequenceBefore, long SequenceAfter, IReadOnlyList<int> CardIds) : IGameEvent;
public sealed record DrawAdviceResolvedEvent(long FrameId, int OwnerDiscardActual,
    int RecipientDiscardActual) : IGameEvent;

internal static class DrawAdviceContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp)) return;
        if (trigger.Window != SkillProgramTriggerWindow.DrawPhaseEnded || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.OwnerRelation is not null ||
            !trigger.Optional || trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null ||
            trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null || trigger.ChoiceGroup is not null ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.EvaluateConditionAtResolution ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0 || trigger.Suits.Count != 0 ||
            trigger.SourceZones.Count != 0 || trigger.DestinationZones.Count != 0 || trigger.DamageOccurrence is not null ||
            trigger.MovementOccurrence is not null || trigger.IncludeResponseUses || trigger.SingleActionInstance ||
            trigger.Effects is not [var select, var terminal] ||
            select.Op != SkillProgramEffectOp.SelectTargets || select.Target != SkillProgramEffectTarget.Owner ||
            select.TargetKind != SkillProgramTargetKind.OtherLiving || select.MinimumTargets != 1 || select.MaximumTargets != 1 ||
            select.TargetAiOrder != SkillProgramTargetAiOrder.SupportDraw || select.NumberExpression is not null ||
            select.Condition.Kind != SkillProgramConditionKind.Always ||
            terminal.Op != SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp ||
            terminal.Target != SkillProgramEffectTarget.SelectedTarget || terminal.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: draw advice requires an optional own DrawPhaseEnded binding, one other living target and one terminal draw/discard instruction.");
    }
}

internal interface IDrawAdviceProgramHost
{
    SkillProgramStepOutcome BeginDrawAdvice(ProgramSkillFrame frame, int recipientSeat);
}
internal sealed class DrawTwoThenDiscardTwoIfOverMaxHpDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawAdviceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.DrawAdvice());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: draw advice requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded), new ReadTargetSet(1, 1)];
}
internal sealed class DrawAdviceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawTwoThenDiscardTwoIfOverMaxHp;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host is IDrawAdviceProgramHost advice
        ? advice.BeginDrawAdvice(frame, targetSeat)
        : throw new InvalidOperationException("The program host does not support draw advice.");
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void DrawAdvice()
    {
        _targetDraw += 2d;
        if (_estimatedHandCount > _player.MaxHp)
        { _ownerDraw -= 2d; _estimatedHandCount = Math.Max(0d, _estimatedHandCount - 2d); }
        if (_publicContext.SelectedTarget is { } target && target.HandCount + 2 > target.MaxHp)
            _targetDraw -= 2d;
    }
}
