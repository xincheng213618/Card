using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum DrawDiscardCategoryStage { Drawing, ChoosingDiscard, DiscardChildren, BonusChildren, Complete }
public sealed record DrawDiscardCategoryMaterial(int CardId, CardLocation From, SkillProgramCardCategory Category);
public sealed record DrawDiscardCategoryDiscard(int CardId, CardLocation From, SkillProgramCardCategory Category, long MovementSequence);

/// <summary>The accepted activation owns its draw, mandatory payment and conditional phase refund.</summary>
public sealed record ProgramDrawDiscardCategoryReceipt
{
    private IReadOnlyList<DrawDiscardCategoryMaterial> _eligible = Array.AsReadOnly(Array.Empty<DrawDiscardCategoryMaterial>());
    private IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<DrawDiscardCategoryDiscard> _actual = Array.AsReadOnly(Array.Empty<DrawDiscardCategoryDiscard>());
    [JsonConstructor]
    public ProgramDrawDiscardCategoryReceipt(int instructionIndex, CardConversionSource source, string gameplayHash,
        string stateId, string usageGroup, int targetSeat, int requestedCount, int actualTurnNumber,
        int actualTurnOwnerSeat, int phaseInstanceId, DrawDiscardCategoryStage stage)
    {
        InstructionIndex = instructionIndex; Source = source; GameplayHash = gameplayHash; StateId = stateId;
        UsageGroup = usageGroup; TargetSeat = targetSeat; RequestedCount = requestedCount;
        ActualTurnNumber = actualTurnNumber; ActualTurnOwnerSeat = actualTurnOwnerSeat;
        PhaseInstanceId = phaseInstanceId; Stage = stage;
    }
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; }
    public string GameplayHash { get; init; }
    public string StateId { get; init; }
    public string UsageGroup { get; init; }
    public int TargetSeat { get; init; }
    public int RequestedCount { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int PhaseInstanceId { get; init; }
    public DrawDiscardCategoryStage Stage { get; init; }
    public bool DrawIssued { get; init; }
    public int DrawActual { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    public int RequiredCount { get; init; }
    public IReadOnlyList<DrawDiscardCategoryMaterial> EligibleMaterials { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedCardIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DrawDiscardCategoryDiscard> ActualDiscards { get => _actual; init => _actual = Array.AsReadOnly(value.ToArray()); }
    public bool DiscardIssued { get; init; }
    public long PaymentBefore { get; init; }
    public long PaymentAfter { get; init; }
    public long? PaymentBatchId { get; init; }
    public bool DistinctNonEmpty { get; init; }
    public bool BonusIssued { get; init; }
    public int BonusActual { get; init; }
    public long BonusBefore { get; init; }
    public long BonusAfter { get; init; }
    public bool QuotaRefunded { get; init; }
    public bool TargetBanned { get; init; }
}

// These facts expose paid entities only after their public discard. The private
// frozen selectable HE set remains exclusively on the owning runtime frame.
public sealed record DrawDiscardCategoryStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string StateId, string UsageGroup, int TargetSeat, int RequestedCount, int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseInstanceId) : IGameEvent;
public sealed record DrawDiscardCategoryDrawIssuedEvent(long FrameId, int TargetSeat, bool Bonus, int RequestedCount,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record DrawDiscardCategoryEntityDiscardedEvent(long FrameId, int TargetSeat, int CardId, CardLocation From,
    SkillProgramCardCategory Category, long MovementSequence) : IGameEvent;
public sealed record DrawDiscardCategoryDiscardPaidEvent(long FrameId, int TargetSeat, int RequiredCount, int ActualDiscardCount,
    bool DistinctNonEmpty, long? BatchId, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record DrawDiscardCategoryRefundedEvent(long FrameId, int OwnerSeat, string SkillId, string StateId,
    string ActivationId, string UsageGroup, int TargetSeat, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int PhaseInstanceId, int BeforeUsage, int AfterUsage) : IGameEvent;
public sealed record DrawDiscardCategoryTargetBannedEvent(long FrameId, int OwnerSeat, string SkillId, string StateId,
    int TargetSeat, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record DrawDiscardCategoryCompletedEvent(long FrameId, bool DistinctNonEmpty, bool BonusIssued,
    bool QuotaRefunded, bool TargetBanned, bool TargetAlive) : IGameEvent;

internal interface IDrawDiscardCategoryProgramHost
{
    SkillProgramStepOutcome DrawThenDiscardDistinctCategories(ProgramSkillFrame frame, int targetSeat, int amount, string stateId);
}
internal sealed class DrawThenDiscardDistinctCategoriesDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardDistinctCategories;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawThenDiscardDistinctCategoriesHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.Draw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "stateId", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target"); var amount = r.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget || amount is < 1 or > 20)
            throw new InvalidOperationException("Draw/discard category refund requires selectedTarget and an amount in 1..20.");
        var effect = new SkillProgramEffect(Op, target, amount, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadTargetSet(1)];
}
public sealed class DrawThenDiscardDistinctCategoriesHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawThenDiscardDistinctCategories;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IDrawDiscardCategoryProgramHost)host).DrawThenDiscardDistinctCategories(frame, seat, effect.Amount, effect.StateId!);
}
internal static class DrawDiscardCategoryComposition
{
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawThenDiscardDistinctCategories)) return;
        if (activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.AnyLiving || activation.UsesPerPhase != 1 ||
            activation.UsesPerTurn is not null || activation.UsesPerGame is not null || activation.Effects.Count != 1 ||
            activation.Effects[0].Condition.Kind != SkillProgramConditionKind.Always || activation.ContinueAfterOwnerDeath ||
            activation.MarkerCost is not null || activation.CardCountExpression is not null || activation.CardKinds.Count != 0 ||
            activation.CardSuits.Count != 0 || activation.CardCategories.Count != 0 || activation.EquipmentSlots.Count != 0 ||
            activation.SelectedCardsSameSuit || activation.SelectedCardsDistinctSuits || activation.TargetRequiresEmptyEquipmentSlot ||
            activation.CategoryTargetLedgerId is not null || activation.TargetPhaseLedgerId is not null)
            throw new InvalidOperationException($"Invalid skill program at {path}: draw/discard refund requires one phase-limited zero-material AnyLiving activation and its sole unconditional operation.");
    }
}
