namespace CardGame.Core;

public enum PublicPileCashOutStage { PaymentChildren, DrawChildren }

/// <summary>A paid instruction owns its original pile, physical invoice and once-issued draw.</summary>
public sealed record PublicPileCashOutReceipt
{
    private IReadOnlyList<int> _paid = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(Array.Empty<CardLocation>());
    public int InstructionIndex { get; init; }
    public SkillProgramEffectOp Operation { get; init; }
    public CardConversionSource Issuer { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurn { get; init; }
    public long ParentId { get; init; }
    public PublicPileCashOutStage Stage { get; init; }
    public PublicPersistentPileSource Pile { get; init; } = null!;
    public int FrozenCount { get; init; }
    public IReadOnlyList<int> PaidCardIds { get => _paid; init => _paid = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<CardLocation> PaidFrom { get => _from; init => _from = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawRequested { get; init; }
    public int DrawActual { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}

// The native movement events already project and freeze physical identities.
// New cash-out facts contain only scalar identities and invoice totals.
public sealed record PublicPileCashOutStartedEvent(long FrameId, SkillProgramEffectOp Operation,
    CardConversionSource Issuer, string GameplayHash, int ActualTurn, long ParentId,
    PublicPersistentPileSource Pile, int FrozenCount) : IGameEvent;
public sealed record PublicPileCashOutPaidEvent(long FrameId, SkillProgramEffectOp Operation,
    int FrozenCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PublicPileCashOutDrawIssuedEvent(long FrameId, int RequestedCount,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PublicPileCashOutResolvedEvent(long FrameId, SkillProgramEffectOp Operation,
    int FrozenCount, int DrawActual, int SlashLimitGranted) : IGameEvent;

internal static class PublicPileCashOutContract
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is
        SkillProgramEffectOp.StoreBoundCardsInPublicPile or SkillProgramEffectOp.CashOutPublicPile;

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => IsOperation(effect.Op))) return;
        var common = trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own &&
            trigger.UsageScope is null && trigger.UsageLimit is null && trigger.DynamicUsageLimit is null &&
            trigger.MarkerCost is null && trigger.ChoiceGroup is null && !trigger.EvaluateConditionAtResolution;
        var storage = trigger.Effects is [var select, var store] &&
            select.Op == SkillProgramEffectOp.SelectOwnedCards && select.Target == SkillProgramEffectTarget.Owner &&
            select.TargetReference is null && select.Condition.Kind == SkillProgramConditionKind.Always &&
            select.Amount == 0 && select.NumberExpression is null && select.MinimumCards == 1 && select.MaximumCards == 1 &&
            select.Zones.Count == 2 && select.Zones.Contains(CardZoneKind.Hand) && select.Zones.Contains(CardZoneKind.Equipment) &&
            select.CardKinds.Count == 0 && select.Suits.Count == 0 && !select.AllowDecline &&
            !string.IsNullOrWhiteSpace(select.ResultBind) && store.Op == SkillProgramEffectOp.StoreBoundCardsInPublicPile &&
            store.Target == SkillProgramEffectTarget.Owner && store.Condition.Kind == SkillProgramConditionKind.Always &&
            store.SourceBind == select.ResultBind && trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
            trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint && trigger.Optional &&
            trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always;
        var cashOut = trigger.Effects is [{ Op: SkillProgramEffectOp.CashOutPublicPile,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] &&
            trigger.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && !trigger.Optional &&
            trigger.Condition is { Kind: SkillProgramTriggerConditionKind.Compare,
                Left: { Kind: SkillProgramTriggerValueKind.CurrentOwnedZoneCount, Zone: CardZoneKind.PublicPersistentPile },
                Comparison: SkillProgramComparisonOperator.GreaterThan,
                Right: { Kind: SkillProgramTriggerValueKind.IntegerConstant, Value: 0 } };
        if (!common || !storage && !cashOut)
            throw new InvalidOperationException($"Invalid skill program at {path}: public pile cash-out requires an optional owner per-damage-point HE selection of exactly one card followed by storage, or one mandatory owner preparation cash-out guarded by a positive exact pile count.");
    }
}

internal sealed class StoreBoundCardsInPublicPileProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreBoundCardsInPublicPile;
    public override ISkillProgramEffectHandler Handler { get; } = new PublicPileCashOutHandler(SkillProgramEffectOp.StoreBoundCardsInPublicPile);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.PublicPileCashOut(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied),
            new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand, CardZoneKind.Equipment]),
            new ReadSingleCardSet(effect.SourceBind!), new ConsumePublicPileCardSet(effect.SourceBind!)];
}

internal sealed class CashOutPublicPileProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.CashOutPublicPile;
    public override ISkillProgramEffectHandler Handler { get; } = new PublicPileCashOutHandler(SkillProgramEffectOp.CashOutPublicPile);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "drawMultiplier", "condition");
        var multiplier = reader.RequiredInt("drawMultiplier");
        if (multiplier is < 1 or > 4)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}: public pile drawMultiplier must be an integer from 1 through 4.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), multiplier, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal interface IPublicPileCashOutHost
{
    SkillProgramStepOutcome ExecutePublicPileCashOut(SkillProgramEffect effect, ProgramSkillFrame frame);
}
internal sealed class PublicPileCashOutHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is IPublicPileCashOutHost pile ? pile.ExecutePublicPileCashOut(effect, frame) :
            throw new InvalidOperationException("The program host does not support public pile cash-out.");
}
