namespace CardGame.Core;

public enum PrepDiscardStage { ChoosingTarget, SelectingTargetCards, TargetChildren, ChoosingBenefit, SelectingOwnerCards, OwnerChildren, Complete }

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
internal sealed class DrawPrepDiscardEndingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPrepDiscardEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawPrepDiscardEndingHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class ResolvePrepDiscardOrEndingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepDiscardOrEnding;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IPrepDiscardEndingProgramHost)h).ResolvePrepDiscardOrEnding(f, e.StateId!);
}
public sealed class DrawPrepDiscardEndingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawPrepDiscardEnding;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IPrepDiscardEndingProgramHost)h).DrawPrepDiscardEnding(f);
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
