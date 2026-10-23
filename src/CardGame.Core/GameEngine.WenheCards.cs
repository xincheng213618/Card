using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WenheCardEffectStage { AwaitingChaos, Children }
public sealed record WenheRedistributionMaterial(int CardId, CardKind PrintedKind, CardLocation From, CardLocation To);
public sealed record WenheCardEffectReceipt(long ActionId, long NullificationFrameId, CardKind Kind,
    int TargetSeat, int TargetIndex, WenheCardEffectStage Stage, int MovementBefore)
{
    private readonly IReadOnlyList<WenheRedistributionMaterial> _materials = Array.AsReadOnly(Array.Empty<WenheRedistributionMaterial>());
    public IReadOnlyList<WenheRedistributionMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public int? SourceHpBefore { get; init; }
    public int? TargetHpBefore { get; init; }
    public int? SourceHpAfter { get; init; }
    public int? TargetHpAfter { get; init; }
    public int? SourceHandCount { get; init; }
    public bool Applied { get; init; }
    public long? MovementBatchId { get; init; }
    public int MovementAfter { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenheCardEffectReceipt? WenheCardEffect { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeWenheCardEffectFrameId { get; init; }
}

// Only public quantities enter history. Hidden hand identities and the complete
// random destination plan stay on the trusted owning card frame.
public sealed record WenheCardPlanIssuedEvent(long CardUseFrameId, long ActionId, long NullificationFrameId,
    CardKind Kind, int SourceSeat, int TargetSeat, int MaterialCount, int? SourceHandCount,
    int? SourceHpBefore, int? TargetHpBefore, int? SourceHpAfter, int? TargetHpAfter) : IGameEvent;
public sealed record WenheCardEffectAppliedEvent(long CardUseFrameId, long ActionId, long NullificationFrameId,
    CardKind Kind, int MovedCards, int MovementBefore, int MovementAfter) : IGameEvent;
public sealed record WenheCardChaosReturnedEvent(long CardUseFrameId, long ActionId, long NullificationFrameId) : IGameEvent;

public sealed partial class GameEngine
{
    private static readonly IReadOnlyList<CardKind> WenheOrdinaryTrickKinds = Array.AsReadOnly(new[]
        { CardKind.StarShift, CardKind.BeamSwap, CardKind.HandRedistribution, CardKind.WenheChaos });
    private static bool IsWenheCard(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_WenheCards.IsWenheCard(kind);
    private static LegalActionKind WenheCardAction(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_WenheCards.WenheCardAction(kind);
    private static CardMoveReason WenheRedistributionReason(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_WenheCards.WenheRedistributionReason(kind);

    private bool CanBeWenheAdditionalTarget(CardUseFrame use, CharacterState target)
    {
        if (!target.IsAlive || target.Seat == use.SourceSeat || !_players[use.SourceSeat].IsAlive) return false;
        return use.CardKind switch
        {
            CardKind.StarShift => target.HasHpValue && target.Hp >= 1 &&
                _players[use.SourceSeat].HasHpValue && _players[use.SourceSeat].Hp >= 1,
            CardKind.HandRedistribution => true,
            _ => false
        };
    }

    private void AddWenheCardActions(List<LegalAction> actions, CharacterState actor, IEnumerable<Card> cards)
    {
        foreach (var card in cards.Where(card => IsWenheCard(card.Kind)))
        {
            var kind = card.Kind;
            if (IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) continue;
            var self = kind is CardKind.BeamSwap or CardKind.WenheChaos;
            foreach (var target in _players.Where(p => p.IsAlive && (self ? p.Seat == actor.Seat : p.Seat != actor.Seat)))
            {
                if (kind == CardKind.StarShift && (!actor.HasHpValue || !target.HasHpValue || actor.Hp < 1 || target.Hp < 1)) continue;
                if (IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) ||
                    IsCardTargetProhibited(target, kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) ||
                    IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, kind) ||
                    HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card))) continue;
                actions.Add(new(WenheCardAction(kind), card.Id, self ? null : target.Seat,
                    self ? $"使用【{CardCatalog.Get(kind).DisplayName}】" : $"对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】"));
            }
        }
    }

    private void ResolveWenheCard(CharacterState actor, Card card, CardKind kind, int? targetSeat = null)
    {
        if (!IsWenheCard(kind) || !BuildLegalActions(actor, selectedPhysicalCardId: card.Id).Any(a =>
                a.Kind == WenheCardAction(kind) && a.CardId == card.Id && a.TargetSeat == targetSeat))
            throw new InvalidOperationException("A Wenhe card lost its published native entity or exact legal target.");
        var seat = kind is CardKind.BeamSwap or CardKind.WenheChaos ? actor.Seat :
            targetSeat ?? throw new InvalidOperationException("A Wenhe single-other trick requires its actual target.");
        var id = BeginCardUse(card, actor.Seat, [seat], kind);
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use,
            disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, [seat], WenheCardAction(kind), playedCardKind: kind);
    }

    private void ResolveWenheCardEffect(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != pending.ParentFrameId ||
            !IsWenheCard(LeChengyanNativeEffectKind(use)) || pending.EffectCardKind != LeChengyanNativeEffectKind(use) || pending.ActionKind != WenheCardAction(LeChengyanNativeEffectKind(use)) ||
            use.SourceSeat != pending.SourceSeat || use.CardId != pending.EffectCardId || use.Action is not { Type: CardActionType.Use } action ||
            action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind || use.WenheCardEffect is not null ||
            GetNullificationTargetSeat(pending) is not { } seat || use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count || LeChengyanNativeTargets(use)[use.TargetIndex] != seat ||
            !IsLeChengyanEffectUse(use.Id) && (LeChengyanNativeEffectKind(use) is CardKind.BeamSwap or CardKind.WenheChaos && seat != use.SourceSeat ||
            LeChengyanNativeEffectKind(use) is CardKind.StarShift or CardKind.HandRedistribution && seat == use.SourceSeat))
            throw new InvalidOperationException("A Wenhe effect lost its exact once-resolved native nullification and target cursor.");
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        if (!_players[seat].IsAlive || !_players[use.SourceSeat].IsAlive ||
            LeChengyanNativeEffectKind(use) == CardKind.StarShift && (!_players[seat].HasHpValue || !_players[use.SourceSeat].HasHpValue || _players[seat].Hp < 1 || _players[use.SourceSeat].Hp < 1))
        { FinishCardUse(use.Id, GetNullificationEffectCard(pending), LeChengyanNativeEffectKind(use)); return; }

        var receipt = new WenheCardEffectReceipt(action.ActionId, pending.Id, LeChengyanNativeEffectKind(use), seat, use.TargetIndex,
            LeChengyanNativeEffectKind(use) == CardKind.WenheChaos ? WenheCardEffectStage.AwaitingChaos : WenheCardEffectStage.Children, _movementSequence)
            { MovementAfter = _movementSequence };
        receipt = LeChengyanNativeEffectKind(use) switch
        {
            CardKind.StarShift => PlanWenheStarShift(use, receipt),
            CardKind.HandRedistribution => PlanWenheHandRedistribution(use, receipt),
            CardKind.BeamSwap => PlanWenheBeamSwap(use, receipt),
            _ => receipt
        };
        ReplaceRuntimeTop(use = use with { Step = ResolutionFrameStep.ResolvingEffect, WenheCardEffect = receipt });
        AdvanceEventRulesAndQueueFact(new WenheCardPlanIssuedEvent(use.Id, action.ActionId, pending.Id, LeChengyanNativeEffectKind(use),
            use.SourceSeat, seat, receipt.Materials.Count, receipt.SourceHandCount, receipt.SourceHpBefore, receipt.TargetHpBefore, receipt.SourceHpAfter, receipt.TargetHpAfter));
        if (LeChengyanNativeEffectKind(use) == CardKind.WenheChaos) { BeginWenheCardChaos(use); return; }
        if (LeChengyanNativeEffectKind(use) == CardKind.StarShift)
        {
            var source = _players[use.SourceSeat]; var target = _players[seat];
            source.Hp = receipt.SourceHpAfter!.Value; target.Hp = receipt.TargetHpAfter!.Value;
            ReplaceRuntimeTop(use with { WenheCardEffect = receipt = receipt with { Applied = true } });
            RecordHpChange(use.Id, use.SourceSeat, source.Seat, receipt.SourceHpBefore!.Value, source.Hp, HpChangeKind.Setting);
            RecordHpChange(use.Id, use.SourceSeat, target.Seat, receipt.TargetHpBefore!.Value, target.Hp, HpChangeKind.Setting);
        }
        else
        {
            var moving = receipt.Materials.Where(m => m.From != m.To).ToArray();
            ReplaceRuntimeTop(use with { WenheCardEffect = receipt = receipt with { Applied = true } });
            if (moving.Length != 0)
            {
                var destinations = moving.ToDictionary(m => m.CardId, m => m.To);
                MoveProgramCardsFromMultipleSources(moving.Select(m => m.CardId).ToArray(), CardLocation.Processing,
                    WenheRedistributionReason(LeChengyanNativeEffectKind(use)), (batchId, records) =>
                    {
                        var current = LifecycleCardUse(use.Id)!;
                        UpdateLifecycleCardUse(use.Id, f => f with { WenheCardEffect = current.WenheCardEffect! with
                            { MovementBatchId = batchId, MovementAfter = records[^1].Sequence } });
                    }, (card, _) => destinations[card.Id],
                    disclosure: LeChengyanNativeEffectKind(use) == CardKind.HandRedistribution ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
            }
        }
        use = LifecycleCardUse(use.Id)!; receipt = use.WenheCardEffect!;
        AdvanceEventRulesAndQueueFact(new WenheCardEffectAppliedEvent(use.Id, receipt.ActionId, receipt.NullificationFrameId,
            LeChengyanNativeEffectKind(use), receipt.Materials.Count(m => m.From != m.To), receipt.MovementBefore, receipt.MovementAfter));
        ContinueWenheCardEffect(use.Id);
    }

    private WenheCardEffectReceipt PlanWenheStarShift(CardUseFrame use, WenheCardEffectReceipt receipt)
    {
        var source = _players[use.SourceSeat]; var target = _players[receipt.TargetSeat];
        var sum = checked(source.Hp + target.Hp);
        var minimum = Math.Max(1, sum - target.MaxHp); var maximum = Math.Min(source.MaxHp, sum - 1);
        if (minimum > maximum) throw new InvalidOperationException("The living HP pair has no total-preserving feasible allocation.");
        var assigned = minimum + _random.Next(maximum - minimum + 1);
        return receipt with { SourceHpBefore = source.Hp, TargetHpBefore = target.Hp, SourceHpAfter = assigned, TargetHpAfter = sum - assigned };
    }

    private WenheCardEffectReceipt PlanWenheHandRedistribution(CardUseFrame use, WenheCardEffectReceipt receipt)
    {
        var cards = GetHand(_players[use.SourceSeat]).Select(card => (Card: card, From: CardLocation.Hand(use.SourceSeat)))
            .Concat(GetHand(_players[receipt.TargetSeat]).Select(card => (Card: card, From: CardLocation.Hand(receipt.TargetSeat))))
            .OrderBy(item => item.Card.Id).ToList();
        _random.Shuffle(cards);
        var sourceCount = _random.Next(cards.Count + 1);
        return receipt with { SourceHandCount = sourceCount, Materials = cards.Select((item, index) => new WenheRedistributionMaterial(item.Card.Id,
            item.Card.Kind, item.From, CardLocation.Hand(index < sourceCount ? use.SourceSeat : receipt.TargetSeat))).ToArray() };
    }

    private WenheCardEffectReceipt PlanWenheBeamSwap(CardUseFrame use, WenheCardEffectReceipt receipt)
    {
        var all = _players.Where(p => p.IsAlive).SelectMany(p => GetEquipment(p).Select(card => (Card: card, Owner: p.Seat))).ToArray();
        var moves = new List<WenheRedistributionMaterial>();
        foreach (var group in all.Where(item => !item.Card.IsGeneralWeapon).GroupBy(item => FieldEquipmentSlot(item.Card)).OrderBy(group => group.Key))
        {
            var tickets = _players.Where(p => p.IsAlive).SelectMany(p => Enumerable.Repeat(p.Seat,
                Math.Max(0, p.EquipmentSlotCapacity(group.Key) - all.Count(item => item.Owner == p.Seat && item.Card.IsGeneralWeapon && FieldEquipmentSlot(item.Card) == group.Key)))).ToList();
            var equipment = group.OrderBy(item => item.Card.Id).ToList();
            if (tickets.Count < equipment.Count) throw new InvalidOperationException("Existing movable equipment exceeds the living legal capacity.");
            _random.Shuffle(tickets); _random.Shuffle(equipment);
            moves.AddRange(equipment.Select((item, index) => new WenheRedistributionMaterial(item.Card.Id, item.Card.Kind,
                CardLocation.Equipment(item.Owner), CardLocation.Equipment(tickets[index]))));
        }
        // One real Ox carries its original private store together with its own
        // destination; the stores are captured before either treasure moves.
        if (UsesFormalWoodenOx)
            foreach (var ox in moves.Where(move => move.PrintedKind == CardKind.WoodenOx && move.From != move.To).ToArray())
                moves.AddRange(_cardZones.CardsAt(CardLocation.WoodenOxGrain(ox.From.OwnerSeat!.Value)).Select(card =>
                    new WenheRedistributionMaterial(card.Id, card.Kind, CardLocation.WoodenOxGrain(ox.From.OwnerSeat.Value), CardLocation.WoodenOxGrain(ox.To.OwnerSeat!.Value))));
        return receipt with { Materials = moves.ToArray() };
    }

    private void CompleteWenheCardChaos(long useId)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { WenheCardEffect: { Stage: WenheCardEffectStage.AwaitingChaos, Applied: false } receipt } use ||
            use.Id != useId || LeChengyanNativeEffectKind(use) != CardKind.WenheChaos || receipt.ActionId != use.Action?.ActionId || _pendingDecision is not null)
            throw new InvalidOperationException("Wenhe Chaos cannot return to a different or already completed native trick.");
        AdvanceEventRulesAndQueueFact(new WenheCardChaosReturnedEvent(useId, receipt.ActionId, receipt.NullificationFrameId));
        ReplaceRuntimeTop(use with { WenheCardEffect = receipt = receipt with { Applied = true, Stage = WenheCardEffectStage.Children } });
        AdvanceEventRulesAndQueueFact(new WenheCardEffectAppliedEvent(useId, receipt.ActionId, receipt.NullificationFrameId, LeChengyanNativeEffectKind(use), 0, receipt.MovementBefore, receipt.MovementAfter));
        ContinueWenheCardEffect(useId);
    }

    private void ContinueWenheCardEffect(long id)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame { WenheCardEffect: { Applied: true, Stage: WenheCardEffectStage.Children } } use || use.Id != id || _pendingDecision is not null)
            throw new InvalidOperationException("Wenhe's paid effect resumed without its exact quiet card owner.");
        AssertWenheCardReceipt(use);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.WenheCardEffect) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.WenheCardEffect) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.WenheCardEffect) || TryBeginCardsMovedProgramWindow(id)) return;
        use = LifecycleCardUse(id)!; AssertWenheCardReceipt(use);
        UpdateLifecycleCardUse(id, f => f with { WenheCardEffect = null });
        FinishCardUse(id, GetTrickRepresentation(id, use.CardId, requireProcessing: true), LeChengyanNativeEffectKind(use));
    }

    private bool IsWenheCardEffectMovement(CardUseFrame use, CardMovementBatchContext batch)
    {
        if (use.WenheCardEffect is not { Applied: true, Stage: WenheCardEffectStage.Children, MovementBatchId: { } batchId } receipt ||
            batch.Id != batchId || batch.ParentFrameId != use.Id || batch.Movements.Count == 0 ||
            batch.Movements.Count != receipt.Materials.Count(m => m.From != m.To) || batch.Movements[0].Sequence != receipt.MovementBefore + 1 || batch.Movements[^1].Sequence != receipt.MovementAfter) return false;
        return batch.Movements.All(move => _cardMovements.Contains(move) && move.Reason == WenheRedistributionReason(LeChengyanNativeEffectKind(use)) &&
            receipt.Materials.Count(m => m.CardId == move.CardId && m.PrintedKind == move.CardKind && m.From == move.From && m.To == move.To && m.From != m.To) == 1);
    }
    private bool IsWenheCardEffectMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame movement) =>
        movement.ResumeWenheCardEffectFrameId == use.Id && movement.Id == movement.Batch.Id && movement.ResumeProgramFrameId is null &&
        !HasAlternateProgramMovementReturn(movement with { ResumeWenheCardEffectFrameId = null }) && IsWenheCardEffectMovement(use, movement.Batch);
    private bool IsWenheCardEffectStateReturn(CardUseFrame use, ProgramLifecycleTriggerWindowFrame state) =>
        use.WenheCardEffect is { Applied: true, Stage: WenheCardEffectStage.Children } && state.ResumeProgramFrameId == use.Id &&
        state.CharacterStateContinuation == CharacterStateContinuation.WenheCardEffect && state.ResumeCardId is null && state.ResumeCardKind is null &&
        ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == state.Id && e.Change.ParentFrameId == use.Id) == 1;
    private bool IsWenheCardEffectHpReturn(CardUseFrame use, HpChangedTriggerWindowFrame hp) =>
        use.WenheCardEffect is { Applied: true, Stage: WenheCardEffectStage.Children } && hp.Continuation == PostEventContinuation.WenheCardEffect &&
        hp.ResumeFrameId == use.Id && hp.Change.ParentFrameId == use.Id && hp.CardId is null && hp.CardKind is null;
    private bool HasWenheCardEffectProcessing(IReadOnlyList<Card> processing)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().ToArray();
        if (!uses.Any(use => use.WenheCardEffect is not null)) return false;
        foreach (var use in uses.Where(use => use.WenheCardEffect is not null)) AssertWenheCardReceipt(use);
        var ids = uses.SelectMany(use => use.PhysicalCardIds ?? (use.CardId == 0 ? [] : new[] { use.CardId })).ToHashSet();
        return processing.All(card => ids.Contains(card.Id));
    }
    private bool IsWenheChaosAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        var producer = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame =>
            frame.WenheChaos?.CardActionId is not null && frame.Id == attack.ProgramSkillCardUseFrameId);
        if (producer?.WenheChaos is not { } chaos || !IsExactWenheChaos(producer) ||
            LifecycleCardUse(chaos.ParentFrameId) is not { Action: { } parentAction } parent ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action } child ||
            !IsSlashCard(action.EffectiveKind) || producer.SelectedTargetSeats is not [var actor] ||
            action.ActorSeat != actor || child.SourceSeat != actor || attack.CardUserSeat != actor ||
            action.ParentActionId != parentAction.ActionId) return false;
        AssertWenheCardReceipt(parent);
        var parentIds = parentAction.PhysicalCards.Select(card => card.CardId).ToHashSet();
        if (!processing.Any(card => parentIds.Contains(card.Id)) || attack.PhysicalCards.Any(card => parentIds.Contains(card.Id))) return false;
        return IsActiveAttackCardConsistent(attack, processing.Where(card => !parentIds.Contains(card.Id)).ToArray());
    }
    private void AssertWenheCardReceipt(CardUseFrame use)
    {
        if (use.WenheCardEffect is not { } r) return;
        if (!IsWenheCard(LeChengyanNativeEffectKind(use)) || r.Kind != LeChengyanNativeEffectKind(use) || use.Action?.ActionId != r.ActionId || r.NullificationFrameId <= 0 ||
            r.TargetIndex != use.TargetIndex || use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count || LeChengyanNativeTargets(use)[use.TargetIndex] != r.TargetSeat ||
            !Enum.IsDefined(r.Stage) || r.Materials is not System.Collections.IList { IsReadOnly: true } || r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence ||
            ProgramEventHistory<WenheCardPlanIssuedEvent>().Count(e => e.CardUseFrameId == use.Id && e.ActionId == r.ActionId && e.NullificationFrameId == r.NullificationFrameId &&
                e.Kind == r.Kind && e.SourceSeat == use.SourceSeat && e.TargetSeat == r.TargetSeat && e.MaterialCount == r.Materials.Count && e.SourceHandCount == r.SourceHandCount &&
                e.SourceHpBefore == r.SourceHpBefore && e.TargetHpBefore == r.TargetHpBefore && e.SourceHpAfter == r.SourceHpAfter && e.TargetHpAfter == r.TargetHpAfter) != 1)
            throw new InvalidOperationException("Wenhe lost its frozen random plan, exact action, target or original material set.");
        if (r.Stage == WenheCardEffectStage.AwaitingChaos)
        {
            if (LeChengyanNativeEffectKind(use) != CardKind.WenheChaos || r.Applied || r.Materials.Count != 0 || r.MovementBatchId is not null) throw new InvalidOperationException("Wenhe Chaos lost its native child return.");
            return;
        }
        if (!r.Applied || LeChengyanNativeEffectKind(use) == CardKind.WenheChaos && ProgramEventHistory<WenheCardChaosReturnedEvent>().Count(e =>
                e.CardUseFrameId == use.Id && e.ActionId == r.ActionId && e.NullificationFrameId == r.NullificationFrameId) != 1 ||
            ProgramEventHistory<WenheCardEffectAppliedEvent>().Count(e => e.CardUseFrameId == use.Id && e.ActionId == r.ActionId &&
                e.NullificationFrameId == r.NullificationFrameId && e.Kind == r.Kind && e.MovedCards == r.Materials.Count(m => m.From != m.To) && e.MovementBefore == r.MovementBefore && e.MovementAfter == r.MovementAfter) != 1 ||
            !_cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter).Select(m => (m.CardId, m.CardKind, m.From, m.To))
                .SequenceEqual(r.Materials.Where(m => m.From != m.To).Select(m => (m.CardId, m.PrintedKind, m.From, m.To))))
            throw new InvalidOperationException("Wenhe changed its already applied entities or repeated a paid redistribution.");
    }
    private void AssertWenheCardState()
    { foreach (var use in _resolutionStack.OfType<CardUseFrame>()) AssertWenheCardReceipt(use); }
}
