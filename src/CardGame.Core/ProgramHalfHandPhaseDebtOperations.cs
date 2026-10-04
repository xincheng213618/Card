namespace CardGame.Core;

public sealed record ProgramHalfHandDrawReceipt(int InstructionIndex, long DrawWindowFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, int ActualTurnNumber, int TurnOwnerSeat, int RequestedCount, int ActualCount,
    long SequenceBefore, long SequenceAfter);
public sealed record HalfHandDrawIssuedEvent(long ProgramFrameId, long DrawWindowFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, int ActualTurnNumber, int TurnOwnerSeat, int RequestedCount, int ActualCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record HalfHandTargetSupportIssuedEvent(long ProgramFrameId, long DrawProgramFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, int ActualTurnNumber, int OwnerSeat, int RecipientSeat, int ActualDeliveredCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record ProgramHalfHandGiftPayment(int InstructionIndex, long DrawProgramFrameId, CardConversionSource Source,
    string GameplayHash, string StateId, int ActualTurnNumber, int TurnOwnerSeat, int RecipientSeat, int HandCountBefore,
    IReadOnlyList<int> CardIds, long SequenceBefore, long SequenceAfter)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ProgramPhaseHandExchangeReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    string StateId, int ActualTurnNumber, int TurnOwnerSeat, int PhaseInstanceId, int FirstSeat, int SecondSeat,
    int FrozenDifference, IReadOnlyList<int> FirstCardIds, IReadOnlyList<int> SecondCardIds,
    long SequenceBefore, long SequenceAfter, bool Issued)
{
    private readonly IReadOnlyList<int> _first = Array.AsReadOnly(FirstCardIds.ToArray());
    public IReadOnlyList<int> FirstCardIds { get => _first; init => _first = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _second = Array.AsReadOnly(SecondCardIds.ToArray());
    public IReadOnlyList<int> SecondCardIds { get => _second; init => _second = Array.AsReadOnly(value.ToArray()); }
}
public sealed record PhaseHandExchangeDebtIssuedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    string StateId, int ActualTurnNumber, int TurnOwnerSeat, int PhaseInstanceId, int FirstSeat, int SecondSeat,
    int FrozenDifference, int FirstCount, int SecondCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record ProgramPhaseHandDebtPayment(int InstructionIndex, string StateId, string ResultBind,
    long ExchangeProgramFrameId, int ActualTurnNumber, int PhaseInstanceId, int FrozenDifference, int RequiredPaymentCount,
    long SequenceBefore);
public sealed record PhaseHandExchangeDebtPaymentStartedEvent(long ProgramFrameId, long ExchangeProgramFrameId,
    CardConversionSource Source, string GameplayHash, string StateId, int ActualTurnNumber, int OwnerSeat,
    int PhaseInstanceId, int FrozenDifference, int RequiredPaymentCount) : IGameEvent;
public sealed record ProgramHalfHandSupportPayment(int InstructionIndex, ActualUseTargetIdentity Use,
    long SupportProgramFrameId, CardConversionSource Source, string GameplayHash, string StateId, int RecipientSeat,
    int? CardId = null, long SequenceBefore = 0, long SequenceAfter = 0, bool Paid = false, bool Declined = false);
public sealed record HalfHandSupportPaymentEvent(long ProgramFrameId, long SupportProgramFrameId, long CardUseFrameId,
    long? CardActionId, CardConversionSource Source, int OwnerSeat, int RecipientSeat, bool Paid, bool Declined,
    long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface IHalfHandPhaseDebtProgramHost
{
    SkillProgramStepOutcome DrawExtraAndArmHalfHandSupport(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome GiveHalfHandAndIssueTargetSupport(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ExchangeHandsAndArmPhaseDebt(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome SelectFrozenHandExchangeDebtPayment(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome OfferHalfHandRecipientSupport(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class HalfHandPhaseDebtDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting), new RequireOwnTurnBoundary()],
        SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterNormalDraw), new RequireOwnTurnBoundary(), new ReadSelectedTarget(), new ReadCardSet(e.SourceBind!)],
        SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt => [new ReadTargetSet(2, 2)],
        SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding), new RequireOwnTurnBoundary(), new CaptureSourceCard(e.ResultBind!, int.MaxValue, false, SkillProgramEffectTarget.Owner)],
        SkillProgramEffectOp.OfferHalfHandRecipientSupport => [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualUseTargeted)],
        _ => throw new InvalidOperationException("Unknown half-hand/phase-debt operation.")
    };
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "amount", "sourceBind", "resultBind", "condition");
        var target = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var state = r.RequiredIdentifier("stateId");
        var amount = Op == SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport ? r.RequiredInt("amount") : 0;
        if (Op == SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport && amount != 2 ||
            Op != SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport && r.Has("amount") ||
            (Op == SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport) != r.Has("sourceBind") ||
            (Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment) != r.Has("resultBind"))
            throw new InvalidOperationException($"Invalid half-hand/phase-debt contract at {r.Path}.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(), stateId: state,
            sourceBind: r.Has("sourceBind") ? r.RequiredIdentifier("sourceBind") : null,
            resultBind: r.Has("resultBind") ? r.RequiredIdentifier("resultBind") : null);
        RequireAlways(effect, r.Path); return effect;
    }
}
internal sealed class DrawExtraAndArmHalfHandSupportDescriptor : HalfHandPhaseDebtDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawExtraAndArmHalfHandSupportHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.Draw(e));
}
internal sealed class GiveHalfHandAndIssueTargetSupportDescriptor : HalfHandPhaseDebtDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveHalfHandAndIssueTargetSupportHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
}
internal sealed class ExchangeHandsAndArmPhaseDebtDescriptor : HalfHandPhaseDebtDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt;
    public override ISkillProgramEffectHandler Handler { get; } = new ExchangeHandsAndArmPhaseDebtHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ExchangeSelectedTargetHands, static (e, c) => c.ExchangeSelectedTargetHands(e));
}
internal sealed class SelectFrozenHandExchangeDebtPaymentDescriptor : HalfHandPhaseDebtDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectFrozenHandExchangeDebtPaymentHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectOwnedCards, static (_, _) => { });
}
internal sealed class OfferHalfHandRecipientSupportDescriptor : HalfHandPhaseDebtDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferHalfHandRecipientSupport;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferHalfHandRecipientSupportHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
}
public sealed class DrawExtraAndArmHalfHandSupportHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => ((IHalfHandPhaseDebtProgramHost)h).DrawExtraAndArmHalfHandSupport(f, e);
}
public sealed class GiveHalfHandAndIssueTargetSupportHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        ((IHalfHandPhaseDebtProgramHost)h).GiveHalfHandAndIssueTargetSupport(f, e);
}
public sealed class ExchangeHandsAndArmPhaseDebtHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => ((IHalfHandPhaseDebtProgramHost)h).ExchangeHandsAndArmPhaseDebt(f, e);
}
public sealed class SelectFrozenHandExchangeDebtPaymentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => ((IHalfHandPhaseDebtProgramHost)h).SelectFrozenHandExchangeDebtPayment(f, e);
}
public sealed class OfferHalfHandRecipientSupportHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferHalfHandRecipientSupport;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => ((IHalfHandPhaseDebtProgramHost)h).OfferHalfHandRecipientSupport(f, e);
}

internal static class HalfHandPhaseDebtComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, int initialTargetSetMaximum, SkillProgramTurnOwnerScope? turnOwnerScope)
    {
        void Fail(string reason) => throw new InvalidOperationException($"Invalid half-hand/phase-debt composition at {path}: {reason}.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport) &&
            (window != SkillProgramTriggerWindow.DrawPhaseStarting || selectedCardCount != 0 ||
             effects is not [{ Op: SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport }])) Fail("one real owner extra-draw instruction is required");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport) &&
            (window != SkillProgramTriggerWindow.AfterNormalDraw || effects is not
                [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLivingLeastHandCount },
                 { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner, NumberExpression: SkillProgramNumberExpression.HandHalfFloor, ResultBind: { } bind } selection,
                 { Op: SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport, SourceBind: { } issued }] ||
             bind != issued || !selection.Zones.SequenceEqual([CardZoneKind.Hand]) || selection.Suits.Count != 0 || selection.CardKinds.Count != 0 ||
             selection.MinimumCards != 0 || selection.MaximumCards != 0 ||
             effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))) Fail("a true unconditional awaited half-hand gift is required");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt) &&
            (window is not null || selectedCardCount != 0 || initialTargetSetMaximum != 2 ||
             effects is not [{ Op: SkillProgramEffectOp.ExchangeHandsAndArmPhaseDebt }])) Fail("a standalone zero-card original pair is required");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment) &&
            (window != SkillProgramTriggerWindow.PlayEnding || effects is not
                [{ Op: SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment, ResultBind: { } cost },
                 { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: { } paid, Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true },
                 { Op: SkillProgramEffectOp.AwaitBoundCardMovements }] || cost != paid ||
             effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always))) Fail("a frozen debt requires an awaited owner HE payment");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.OfferHalfHandRecipientSupport) &&
            (window != SkillProgramTriggerWindow.OtherActualUseTargeted ||
             effects is not [{ Op: SkillProgramEffectOp.OfferHalfHandRecipientSupport }])) Fail("a true target window requires one recipient-owned gift offer");
        if (effects.Any(e => e.Op is SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport or SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport or
            SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment) &&
            (turnOwnerScope ?? SkillProgramTurnOwnerScope.Own) != SkillProgramTurnOwnerScope.Own) Fail("the actual owner's phase is required");
    }
}

internal static class HalfHandPhaseDebtTriggerContract
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerSubject subject, bool optional)
    {
        if (effects.Any(e => e.Op is SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport or SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport or
            SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment or SkillProgramEffectOp.OfferHalfHandRecipientSupport) &&
            (subject != SkillProgramTriggerSubject.Owner || optional && effects.Any(e => e.Op is SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport or
                SkillProgramEffectOp.SelectFrozenHandExchangeDebtPayment or SkillProgramEffectOp.OfferHalfHandRecipientSupport)))
            throw new InvalidOperationException($"Invalid half-hand/phase-debt trigger at {path}: the owner and mandatory paid/gift boundary are required.");
    }
}
