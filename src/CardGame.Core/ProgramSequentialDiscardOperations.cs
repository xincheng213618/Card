namespace CardGame.Core;

public enum ProgramSequentialDiscardKind { CategoryOrSequential, SelectedStartEscalating }
public enum ProgramSequentialDiscardStage { BranchChoice, CardChoice, SelectingBatch, AwaitingMovement, AwaitingDamage, Complete }

public sealed record ProgramSequentialDiscardPayment(int ChooserSeat, int Cursor,
    IReadOnlyList<int> CardIds, IReadOnlyList<CardLocation> SourceLocations,
    long SequenceBefore, long SequenceAfter, int ActualCount)
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _sourceLocations = Array.AsReadOnly(SourceLocations.ToArray());
    public IReadOnlyList<CardLocation> SourceLocations { get => _sourceLocations; init => _sourceLocations = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ProgramSequentialDiscardTopPayment(int InstructionIndex, string ResultBind, int CardId,
    CardLocation SourceLocation, long MovementSequenceBefore, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int TargetSeat);
public sealed record ProgramSequentialDiscardDraft(ProgramSequentialDiscardKind Kind, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int StartSeat, IReadOnlyList<int> Order, int Cursor, int ChooserSeat, int PreviousCount, int Remaining,
    bool? PrimaryBranch, ProgramSequentialDiscardStage Stage,
    IReadOnlyList<int> SelectedCardIds, IReadOnlyList<CardLocation> SelectedLocations,
    ProgramSequentialDiscardPayment? Payment = null)
{
    private readonly IReadOnlyList<int> _order = Array.AsReadOnly(Order.ToArray());
    public IReadOnlyList<int> Order { get => _order; init => _order = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _selectedCardIds = Array.AsReadOnly(SelectedCardIds.ToArray());
    public IReadOnlyList<int> SelectedCardIds { get => _selectedCardIds; init => _selectedCardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _selectedLocations = Array.AsReadOnly(SelectedLocations.ToArray());
    public IReadOnlyList<CardLocation> SelectedLocations { get => _selectedLocations; init => _selectedLocations = Array.AsReadOnly(value.ToArray()); }
}

// No private selection IDs or collection-bearing payload enters public history.
public sealed record ProgramSequentialDiscardStartedEvent(long FrameId, ProgramSequentialDiscardKind Kind,
    CardConversionSource Source, string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int StartSeat) : IGameEvent;
public sealed record ProgramSequentialDiscardTopPaymentIssuedEvent(long FrameId, long MovementSequenceBefore,
    CardConversionSource Source, string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int TargetSeat) : IGameEvent;
public sealed record ProgramSequentialDiscardBranchEvent(long FrameId, int Cursor, int ChooserSeat, bool PrimaryBranch, int RequiredCount) : IGameEvent;
public sealed record ProgramSequentialDiscardPaidEvent(long FrameId, int Cursor, int ChooserSeat,
    long SequenceBefore, long SequenceAfter, int SelectedCount, int ActualCount, int RemainingAfter) : IGameEvent;
public sealed record ProgramSequentialDiscardDamageChosenEvent(long FrameId, int Cursor, int SourceSeat,
    int TargetSeat, int Amount, DamageNature Nature) : IGameEvent;
public sealed record ProgramSequentialDiscardFinishedEvent(long FrameId, int ProcessedSeats, int RemainingUnpaid) : IGameEvent;

internal interface ISequentialDiscardProgramHost
{
    SkillProgramStepOutcome RunSequentialDiscard(ProgramSkillFrame frame, SkillProgramEffect effect, int selectedTarget);
}
internal abstract class SequentialDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "secondaryAmount", "cardCategories", "zones", "nature", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        var secondary = r.Has("secondaryAmount") ? r.RequiredInt("secondaryAmount") : 0;
        var categories = r.OptionalEnumArray<SkillProgramCardCategory>("cardCategories") ?? [];
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        if (amount is < 1 or > 64 ||
            zones.Count == 0 || zones.Distinct().Count() != zones.Count || zones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            Op == SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard &&
                (target != SkillProgramEffectTarget.SelectedTarget || amount != 1 || secondary is < 1 or > 64 || categories.Count != 1 || r.Has("nature")) ||
            Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected &&
                (target != SkillProgramEffectTarget.Owner || secondary != 0 || categories.Count != 0))
            throw new InvalidOperationException($"Invalid sequential discard contract at {r.Path}.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(), minimumValue: secondary,
            zones: zones, cardCategories: categories, damageNature: r.Has("nature") ? r.RequiredEnum<DamageNature>("nature") : null);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected ? [new ReadSelectedTarget()] : WithSelectedTarget(effect);
}
internal sealed class ChooseCategoryOrSequentialDiscardDescriptor : SequentialDiscardDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseCategoryOrSequentialDiscardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.CategoryAlternativeDiscard(effect));
}
internal sealed class EscalatingDiscardOrDamageFromSelectedDescriptor : SequentialDiscardDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected;
    public override ISkillProgramEffectHandler Handler { get; } = new EscalatingDiscardOrDamageFromSelectedHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.EscalatingDiscardOrDamage(effect));
}
public sealed class ChooseCategoryOrSequentialDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ISequentialDiscardProgramHost)host).RunSequentialDiscard(f, e, seat);
}
public sealed class EscalatingDiscardOrDamageFromSelectedHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ISequentialDiscardProgramHost)host).RunSequentialDiscard(f, e, seat);
}

internal static class SequentialDiscardComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window,
        int selectedCardCount, bool selectedTarget, int initialTargetSetMaximum,
        IReadOnlyList<CardZoneKind>? sourceZones, IReadOnlyList<SkillProgramCardCategory>? sourceCategories)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard) &&
            (window is not null || !selectedTarget || initialTargetSetMaximum != 0 || selectedCardCount != 1 ||
             sourceZones is not [CardZoneKind.Hand] || sourceCategories is not [SkillProgramCardCategory.Trick] ||
             effects is not [{ Op: SkillProgramEffectOp.CaptureSelectedCards, Target: SkillProgramEffectTarget.Owner, ResultBind: { } bind, Condition.Kind: SkillProgramConditionKind.Always },
                 { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: { } moved, Destination: SkillProgramCardDestination.DrawPileTop,
                   AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always },
                 { Op: SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard }] || bind != moved))
            throw new InvalidOperationException($"{path}: category/sequential discard requires one real captured hand category top-payment and one original target.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected) &&
            (window is not null || selectedCardCount != 0 || !selectedTarget || initialTargetSetMaximum != 0 ||
             effects is not [{ Op: SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected, Target: SkillProgramEffectTarget.Owner }]))
            throw new InvalidOperationException($"{path}: chosen-start escalating damage requires one standalone zero-card one-target activation.");
        if (effects.Any(e => e.TargetKind == SkillProgramTargetKind.OtherLivingHandAtMostOwner) &&
            (window != SkillProgramTriggerWindow.TurnEnding || selectedCardCount != 0 || selectedTarget ||
             effects is not [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.OtherLivingHandAtMostOwner },
                 { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget }]))
            throw new InvalidOperationException($"{path}: current at-most-owner hand selection is a two-node Ending damage contract.");
    }
}
