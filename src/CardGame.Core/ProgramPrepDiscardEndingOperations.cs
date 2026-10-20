namespace CardGame.Core;

public enum PrepDiscardStage { ChoosingTarget, SelectingTargetCards, TargetChildren, ChoosingBenefit, SelectingOwnerCards, OwnerChildren, Complete,
    ImmediateDrawAttempt, ImmediateDrawChildren }

public sealed record PrepDiscardImmediateDrawReceipt(int TargetSeat, int RequestedCount)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawOutcome? Outcome { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Cancelled { get; init; }
}

public sealed record PrepDiscardPayment(int PayerSeat, IReadOnlyList<int> CardIds,
    IReadOnlyList<CardLocation> From, long SequenceBefore, long SequenceAfter, int ActualCount, int NonEquipmentCount)
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _from = Array.AsReadOnly(From.ToArray());
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
}
public sealed record PrepDiscardDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, long PrepWindowId, PrepDiscardStage Stage,
    int? TargetSeat, int FrozenHandCount, int FrozenHp, int RequestedCount, int RequiredCount,
    IReadOnlyList<int> SelectedCardIds, IReadOnlyList<CardLocation> SelectedFrom,
    PrepDiscardPayment? TargetPayment = null, PrepDiscardPayment? OwnerPayment = null, bool? Deferred = null)
{
    private readonly IReadOnlyList<int> _ids = Array.AsReadOnly(SelectedCardIds.ToArray());
    public IReadOnlyList<int> SelectedCardIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _from = Array.AsReadOnly(SelectedFrom.ToArray());
    public IReadOnlyList<CardLocation> SelectedFrom { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PrepDiscardImmediateDrawReceipt? ImmediateDraw { get; init; }
}
// Every public field is a scalar or a scalar-only source. Hidden selected cards
// remain on the trusted owning frame until actual mature discard exposes them.
public sealed record PrepDiscardTargetFrozenEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, long PrepWindowId, int TargetSeat,
    int HandCount, int Hp, int RequestedCount, int RequiredCount) : IGameEvent;
public sealed record PrepDiscardPaidEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int PayerSeat, bool OwnerCost,
    long SequenceBefore, long SequenceAfter, int ActualCount, int NonEquipmentCount) : IGameEvent;
public sealed record PrepDiscardBenefitChosenEvent(long ProgramFrameId, bool Deferred, int TargetSeat, int Count) : IGameEvent;
public sealed record PrepDiscardFinishedEvent(long ProgramFrameId, bool Completed) : IGameEvent;
public sealed record PrepDiscardImmediateBenefitChosenEvent(long ProgramFrameId, bool DrawTarget, int TargetSeat, int Count) : IGameEvent;
public sealed record PrepDiscardImmediateDrawRequestedEvent(long ProgramFrameId, int TargetSeat, int RequestedCount) : IGameEvent;
public sealed record PrepDiscardImmediateDrawPaidEvent(long ProgramFrameId, int TargetSeat, int RequestedCount, int ActualCount,
    long SequenceBefore, long SequenceAfter, long? AttemptFrameId, bool Replaced, bool Cancelled = false) : IGameEvent;

public sealed record PrepDiscardEndingPromise(long Id, long ProducerProgramFrameId, long PrepWindowId,
    CardConversionSource PaidSource, string PaidGameplayHash, CardConversionSource EndingSource, string EndingGameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int TargetSeat, int Count, long PaymentBefore, long PaymentAfter);
public sealed record PrepDiscardEndingIssuedEvent(PrepDiscardEndingPromise Promise) : IGameEvent;
public sealed record PrepDiscardEndingConsumedEvent(long Id, int ActualTurnNumber, int ActualTurnOwnerSeat, bool Applied) : IGameEvent;
public sealed record PrepDiscardEndingDrawnEvent(long ProgramFrameId, long PromiseId, int TargetSeat, int RequestedCount,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PrepDiscardEndingDrawReceipt(PrepDiscardEndingPromise Promise, long SequenceBefore,
    long SequenceAfter, int ActualCount, bool ChildrenCompleted = false);

internal interface IPrepDiscardEndingProgramHost
{
    SkillProgramStepOutcome ResolvePrepDiscardOrEnding(ProgramSkillFrame frame, string endingBinding);
    SkillProgramStepOutcome ResolvePrepDiscardOrImmediateDraw(ProgramSkillFrame frame);
    SkillProgramStepOutcome DrawPrepDiscardEnding(ProgramSkillFrame frame);
}
internal sealed class ResolvePrepDiscardOrEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepDiscardOrEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolvePrepDiscardOrEndingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,
        static (e, c) => c.ChooseOtherOwnedCardDiscard(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "zones", "condition");
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        if (!zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]))
            throw new InvalidOperationException($"{r.Path}: preparation discard requires precisely Hand/Equipment.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"), zones: zones);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class ResolvePrepDiscardOrImmediateDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolvePrepDiscardOrImmediateDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,
        static (e, c) => c.ChooseOtherOwnedCardDiscard(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "condition");
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        if (!zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]))
            throw new InvalidOperationException($"{r.Path}: preparation discard requires precisely Hand/Equipment.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), zones: zones);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw)) return;
        if (trigger.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || !trigger.Optional ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.UsageScope is not null || trigger.UsageLimit is not null ||
            trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null || trigger.ChoiceGroup is not null ||
            trigger.GainPhaseQualification is not null || trigger.RequiredPolarity is not null || trigger.MarkerCost is not null ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw }])
            throw new InvalidOperationException($"{path}: immediate preparation discard is one own optional actual Prep instruction.");
    }

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.PrepDiscard?.TargetPayment is not null && context.PaidReturnAccepted;
}
public sealed class ResolvePrepDiscardOrImmediateDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepDiscardOrImmediateDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IPrepDiscardEndingProgramHost>().ResolvePrepDiscardOrImmediateDraw(f);
}
internal sealed class DrawPrepDiscardEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPrepDiscardEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawPrepDiscardEndingHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class ResolvePrepDiscardOrEndingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepDiscardOrEnding;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IPrepDiscardEndingProgramHost>().ResolvePrepDiscardOrEnding(f, e.StateId!);
}
public sealed class DrawPrepDiscardEndingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPrepDiscardEnding;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IPrepDiscardEndingProgramHost>().DrawPrepDiscardEnding(f);
}
internal static class PrepDiscardEndingComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope, bool optional)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePrepDiscardOrEnding) &&
            (window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner ||
             scope != SkillProgramTurnOwnerScope.Own || !optional || effects is not [{ Op: SkillProgramEffectOp.ResolvePrepDiscardOrEnding }]))
            throw new InvalidOperationException($"{path}: preparation discard is one own optional actual Prep instruction.");
        if ((scope == SkillProgramTurnOwnerScope.PaidPrepDiscardEnding || effects.Any(e => e.Op == SkillProgramEffectOp.DrawPrepDiscardEnding)) &&
            (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner || optional ||
             scope != SkillProgramTurnOwnerScope.PaidPrepDiscardEnding || effects is not [{ Op: SkillProgramEffectOp.DrawPrepDiscardEnding }]))
            throw new InvalidOperationException($"{path}: payment-derived draws require one exact mandatory promised Ending instruction.");
    }
}
