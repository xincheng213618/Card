using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum OffTurnUsedCardKind { CardUse, DodgeUse, NullificationUse }
public enum OffTurnUsedCardGiftStage { ChoosingRecipient, GiftChildren }

/// <summary>One real completion movement, captured before its native use returns.</summary>
public sealed record OffTurnUsedDiscardEntity(int CardId, CardKind PrintedKind, CardLocation OriginalFrom,
    long NativeBatchId, int MovementSequence);

// A legacy zero-entity Slash can have no CardAction until completion. Preserve
// its real native response parent at acceptance rather than guessing one later.
public sealed record OffTurnResponseUseOriginEvent(long CardUseFrameId, long ActionId, int ActorSeat,
    int ProviderSeat, int OpponentSeat, CardKind ParentKind, OffTurnUsedCardKind Kind) : IGameEvent;

public sealed record OffTurnUsedMaterialDiscardedEvent(long AnchorBatchId, long CardUseFrameId, long ActionId,
    int ActorSeat, int ProviderSeat, CardKind EffectiveKind, OffTurnUsedCardKind Kind,
    int ActualTurnNumber, int ActualTurnOwnerSeat, CardConversionSource Source, string GameplayHash,
    OffTurnUsedDiscardEntity Entity) : IGameEvent;

public sealed record ProgramOffTurnUsedCardGiftReceipt(int InstructionIndex, long WindowFrameId,
    long AnchorBatchId, long CardUseFrameId, long ActionId, CardKind EffectiveKind, OffTurnUsedCardKind Kind,
    int ActualTurnNumber, int ActualTurnOwnerSeat, CardConversionSource Source, string GameplayHash,
    IReadOnlyList<OffTurnUsedDiscardEntity> OriginalEntities, IReadOnlyList<int> CandidateSeats,
    OffTurnUsedCardGiftStage Stage, int? RecipientSeat = null, IReadOnlyList<int>? PaidCardIds = null,
    long GiftBatchId = 0, long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<OffTurnUsedDiscardEntity> _originalEntities = Array.AsReadOnly(OriginalEntities.ToArray());
    public IReadOnlyList<OffTurnUsedDiscardEntity> OriginalEntities { get => _originalEntities; init => _originalEntities = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _candidateSeats = Array.AsReadOnly(CandidateSeats.ToArray());
    public IReadOnlyList<int> CandidateSeats { get => _candidateSeats; init => _candidateSeats = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int>? _paidCardIds = PaidCardIds is null ? null : Array.AsReadOnly(PaidCardIds.ToArray());
    public IReadOnlyList<int>? PaidCardIds { get => _paidCardIds; init => _paidCardIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramOffTurnUsedCardGiftReceipt? OffTurnUsedCardGift { get; init; }
}

// Each payload contains only immutable scalar records; no event list needs a projection bypass.
public sealed record OffTurnUsedCardGiftStartedEvent(long FrameId, long WindowFrameId, long AnchorBatchId,
    long CardUseFrameId, long ActionId, CardConversionSource Source, string GameplayHash, int MaterialCount) : IGameEvent;
public sealed record OffTurnUsedCardGiftPaidEvent(long FrameId, long ActionId, int RecipientSeat,
    int CardCount, long GiftBatchId, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record OffTurnUsedCardGiftResolvedEvent(long FrameId, long ActionId, int RecipientSeat,
    int CardCount, long GiftBatchId) : IGameEvent;

internal interface IOffTurnUsedCardsProgramHost
{
    SkillProgramStepOutcome GiveOffTurnUsedCards(ProgramSkillFrame frame);
}

internal sealed class GiveOffTurnUsedCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveOffTurnUsedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveOffTurnUsedCardsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiftSettledUsedCard,
        static (e, c) => c.GiftSettledUsedCard(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path);
        return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)];
}

internal sealed class GiveOffTurnUsedCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveOffTurnUsedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        ((IOffTurnUsedCardsProgramHost)host).GiveOffTurnUsedCards(f);
}

internal static class OffTurnUsedCardsComposition
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveOffTurnUsedCards)) return;
        if (t.Window != SkillProgramTriggerWindow.DiscardPileReceived || t.Subject != SkillProgramTriggerSubject.Owner ||
            !t.Optional || t.DiscardOwnerScope != SkillProgramDiscardOwnerScope.Own || t.MovementDiscardOnly ||
            t.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch ||
            t.Effects is not [{ Op: SkillProgramEffectOp.GiveOffTurnUsedCards, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }] || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.SourceSkillId is not null || t.SourceViewAsId is not null || t.Suits.Count != 0 || t.MinimumRank != 1 || t.MaximumRank != 13 ||
            t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
            t.CardKinds.Count != 0 || t.DamageCardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 ||
            t.DestinationZones.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.IgnoreOwnSkillMovements || t.DamageOccurrence is not null || t.DrawPhaseMode != SkillProgramDrawPhaseMode.Additive ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
            t.GainPhaseQualification is not null || t.RequireDamageSource is not null || t.RequireNoCardConversion is not null ||
            t.ChoiceGroup is not null || t.OwnerRelation is not null || t.AllowNoEventTarget || t.IncludeResponseUses || t.SingleActionInstance ||
            t.NoDyingAtActivation || t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded ||
            t.EvaluateConditionAtResolution || t.MarkerCost is not null || t.HpChangeOccurrence != SkillProgramHpChangeOccurrence.PerEvent)
            throw new InvalidOperationException($"Invalid skill program at {path}: off-turn used-card gifts require one optional unfiltered own discard batch and their exact native actual-use proof.");
    }
}
