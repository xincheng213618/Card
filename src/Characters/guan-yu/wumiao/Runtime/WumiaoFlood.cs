using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WumiaoFloodStage { AwaitingDamage, AfterDamage, ChoosingDiscard, DiscardChildren, Drawing, DrawingChildren, FinishingAttack }
public sealed record WumiaoFloodGrainMaterial(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record WumiaoFloodDiscardMovement(int CardId, CardKind PrintedKind, CardLocation From,
    CardLocation To, CardMoveReason Reason, int MovementSequence);
public sealed record WumiaoFloodDiscardBatch(long BatchId, long? ParentBatchId, int MovementCount);

// The anniversary card is distinct from the national-war card with the same name.
public sealed record WumiaoFloodReceipt(long ActionId, long NullificationFrameId, int TargetSeat,
    int TargetIndex, int ExtraEffectOrdinal, WumiaoFloodStage Stage)
{
    public long? AwaitingProgramFrameId { get; init; }
    public int? DiscardCardId { get; init; }
    public CardLocation? DiscardFrom { get; init; }
    public CardLocation? DiscardTo { get; init; }
    public CardKind? DiscardPrintedKind { get; init; }
    public int DiscardSequence { get; init; }
    public int DiscardSequenceBefore { get; init; }
    public int DiscardSequenceAfter { get; init; }
    public bool DiscardInvoiceSealed { get; init; }
    public bool DiscardNativeDestruction { get; init; }
    private readonly IReadOnlyList<WumiaoFloodGrainMaterial> _grainMaterials = Array.AsReadOnly(Array.Empty<WumiaoFloodGrainMaterial>());
    public IReadOnlyList<WumiaoFloodGrainMaterial> GrainMaterials
    { get => _grainMaterials; init => _grainMaterials = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<WumiaoFloodDiscardMovement> _discardMovements = Array.AsReadOnly(Array.Empty<WumiaoFloodDiscardMovement>());
    public IReadOnlyList<WumiaoFloodDiscardMovement> DiscardMovements
    { get => _discardMovements; init => _discardMovements = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<WumiaoFloodDiscardBatch> _discardBatches = Array.AsReadOnly(Array.Empty<WumiaoFloodDiscardBatch>());
    public IReadOnlyList<WumiaoFloodDiscardBatch> DiscardBatches
    { get => _discardBatches; init => _discardBatches = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? DrawOutcome { get; init; }
}

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WumiaoFloodReceipt? WumiaoFlood { get; init; }
}

public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeWumiaoFloodFrameId { get; init; }
}

public sealed record WumiaoFloodEffectStartedEvent(long UseId, long ActionId, long NullificationId,
    int TargetSeat, int TargetIndex, int ExtraEffectOrdinal) : IGameEvent;
public sealed record WumiaoFloodDamageReturnedEvent(long UseId, long NullificationId, int TargetSeat,
    int TargetIndex) : IGameEvent;
public sealed record WumiaoFloodDiscardPaidEvent(long UseId, long NullificationId, int TargetSeat,
    int CardId, CardLocation From, int MovementSequence) : IGameEvent;
public sealed record WumiaoFloodDiscardInvoiceSealedEvent(long UseId, long NullificationId, int PrimarySequence,
    int SequenceBefore, int SequenceAfter, int GrainCount, int MovementCount, int BatchCount) : IGameEvent;
public sealed record WumiaoFloodDiscardMaterialPaidEvent(long UseId, long NullificationId, int Index,
    int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, CardMoveReason Reason, int MovementSequence) : IGameEvent;
public sealed record WumiaoFloodDiscardBatchIssuedEvent(long UseId, long NullificationId, long BatchId,
    long? ParentBatchId, int MovementCount) : IGameEvent;
public sealed record WumiaoFloodDrawReturnedEvent(long UseId, long NullificationId, int RecipientSeat,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason WumiaoFloodDiscardReason = new("card.effect.wumiao-flood.discard");

    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 28510)]
    private void AddWumiaoFloodNaturalActions(List<LegalAction> actions, CharacterState actor,
        IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        foreach (var card in playable.Where(card => card.Kind == CardKind.WumiaoFlood &&
                     (selectedPhysicalCardId is null || card.Id == selectedPhysicalCardId) && !HasProgramCardIdentity(actor, card)))
        foreach (var option in BuildProgramOrdinaryTrickUseOptions(actor, CardKind.WumiaoFlood,
                     EffectiveSuit(actor, card), enforceUsePermission: true, physicalCardIds: [card.Id]))
            actions.Add(new LegalAction(option.ActionKind, card.Id, option.TargetSeats.Count == 1 ? option.TargetSeats[0] : null,
                option.Description, TargetSeats: option.TargetSeats));
    }

    private bool TryExecuteWumiaoFloodNaturalUse(CharacterState actor, Card card, LegalAction action)
    {
        if (card.Kind != CardKind.WumiaoFlood || action.Kind != LegalActionKind.WumiaoFlood ||
            action.ConversionSource is not null || action.PlayedCardKind is not null) return false;
        if (!BuildLegalActions(actor, selectedPhysicalCardId: card.Id).Any(candidate => candidate.Kind == action.Kind &&
                candidate.CardId == action.CardId && candidate.ConversionSource is null &&
                candidate.PlayedCardKind is null && candidate.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("The physical Wumiao Flood lost its actual ordered targets.");
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats);
        MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use,
            disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, action.TargetSeats, LegalActionKind.WumiaoFlood);
        return true;
    }

    private void ResolveWumiaoFloodEffect(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != pending.ParentFrameId ||
            LeChengyanNativeEffectKind(use) != CardKind.WumiaoFlood || pending.ActionKind != LegalActionKind.WumiaoFlood ||
            pending.EffectCardKind != LeChengyanNativeEffectKind(use) || use.WumiaoFlood is not null ||
            GetNullificationTargetSeat(pending) is not { } target || use.TargetIndex >= LeChengyanNativeTargets(use).Count ||
            LeChengyanNativeTargets(use)[use.TargetIndex] != target || use.Action is not { Type: CardActionType.Use } action)
            throw new InvalidOperationException("Wumiao Flood lost its exact resolved target and owning use.");
        if (!_players[target].IsAlive)
        {
            FinishCardUse(use.Id, GetNullificationEffectCard(pending), LeChengyanNativeEffectKind(use));
            return;
        }
        var receipt = new WumiaoFloodReceipt(action.ActionId, pending.Id, target, use.TargetIndex,
            NativeExtraEffectRoundOrdinal(use.Id), WumiaoFloodStage.AwaitingDamage)
        { AwaitingProgramFrameId = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is { } parent &&
                IsAwaitingProgramMovement(parent) ? parent.Id : null };
        ReplaceRuntimeTop(use with { WumiaoFlood = receipt });
        AdvanceEventRulesAndQueueFact(new WumiaoFloodEffectStartedEvent(use.Id, action.ActionId, pending.Id,
            target, use.TargetIndex, receipt.ExtraEffectOrdinal));
        if (!IsExtraCardEffectRound(use.Id))
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(use.CardId, LeChengyanNativeEffectKind(use), use.SourceSeat, target));
        var card = GetNullificationEffectCard(pending);
        var attack = new CardAttackHandle(this, use.Id, use.SourceSeat, target, card,
            playedCardKind: CardKind.WumiaoFlood, damageNatureOverride: DamageNature.Thunder,
            physicalCards: GetCardUsePhysicalCards(use.Id));
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }

    private bool TryCompleteWumiaoFloodAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { WumiaoFlood: { } receipt } use) return false;
        if (LeChengyanNativeEffectKind(use) != CardKind.WumiaoFlood || attack.EffectiveCardKind != LeChengyanNativeEffectKind(use) ||
            use.Action?.ActionId != receipt.ActionId || attack.CardUserSeat != use.SourceSeat ||
            use.TargetIndex != receipt.TargetIndex || ActiveDying is not null ||
            _resolutionStack.LastOrDefault()?.Id != use.Id)
            throw new InvalidOperationException("Wumiao Flood lost its completed damage subtree.");
        if (receipt.Stage == WumiaoFloodStage.FinishingAttack)
        {
            UpdateLifecycleCardUse(use.Id, current => current with { WumiaoFlood = null });
            return false;
        }
        if (receipt.Stage != WumiaoFloodStage.AwaitingDamage)
            throw new InvalidOperationException("Wumiao Flood returned native damage twice.");
        ActiveCardAttack = null;
        ActiveDuel = null;
        ClearPendingDecision();
        use = LifecycleCardUse(use.Id)!;
        ReplaceRuntimeTop(use with { WumiaoFlood = receipt with { Stage = WumiaoFloodStage.AfterDamage } });
        AdvanceEventRulesAndQueueFact(new WumiaoFloodDamageReturnedEvent(use.Id, receipt.NullificationFrameId,
            receipt.TargetSeat, receipt.TargetIndex));
        ContinueWumiaoFlood(use.Id);
        return true;
    }

    private IReadOnlyList<Card> WumiaoFloodDiscardable(int target) => GetHand(_players[target])
        .Concat(GetEquipment(_players[target])).Where(card => !IsSelfHandCategoryDiscardForbidden(
            target, card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard)).ToArray();

    private void BeginWumiaoFloodDiscard(CardUseFrame use)
    {
        var receipt = use.WumiaoFlood!;
        var choices = WumiaoFloodDiscardable(receipt.TargetSeat).Select(card => new PromptChoice(
            new ChoiceId($"wumiao-flood.{use.Id}.{receipt.NullificationFrameId}.discard-{card.Id}"),
            $"弃置【{card.DisplayName}】", [card.Id], [], new Dictionary<string, string>
            { ["program-action"] = "wumiao-flood-discard", ["frame-id"] = use.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
        if (choices.Length == 0) throw new InvalidOperationException("Wumiao Flood cannot ask for an unavailable discard.");
        _pendingDecision = new(DecisionKind.ProgramTrigger, receipt.TargetSeat, "【水淹七军】请弃置一张牌。",
            choices.SelectMany(c => c.Cards).ToArray(), [], use.SourceSeat, CardKind.WumiaoFlood)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = Array.AsReadOnly(choices) };
        _status = _players[receipt.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool HasWumiaoFloodDiscardPrompt() => _resolutionStack.LastOrDefault() is CardUseFrame
        { WumiaoFlood: { Stage: WumiaoFloodStage.ChoosingDiscard } r } use &&
        _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IncomingCard: CardKind.WumiaoFlood } prompt &&
        prompt.PlayerSeat == r.TargetSeat && prompt.SourceSeat == use.SourceSeat;

    [ProgramAnswer("wumiao-flood-discard")]
    private void ResolveWumiaoFloodDiscard(PromptChoice selected)
    {
        if (!HasWumiaoFloodDiscardPrompt() || _resolutionStack.LastOrDefault() is not CardUseFrame { WumiaoFlood: { } r } use ||
            selected.Cards is not [var cardId] || selected.Targets.Count != 0 ||
            selected.Parameters.GetValueOrDefault("frame-id") != use.Id.ToString(CultureInfo.InvariantCulture) ||
            !_pendingDecision!.Choices.Any(choice => choice.Id == selected.Id && choice.Cards.SequenceEqual(selected.Cards)) ||
            WumiaoFloodDiscardable(r.TargetSeat).SingleOrDefault(card => card.Id == cardId) is not { } card)
            throw new InvalidOperationException("Wumiao Flood changed its exact target's mandatory discard.");
        var from = _cardZones.GetLocation(cardId);
        var before = _movementSequence;
        var grains = UsesFormalWoodenOx && card.Kind == CardKind.WoodenOx && from == CardLocation.Equipment(r.TargetSeat)
            ? _cardZones.CardsAt(CardLocation.WoodenOxGrain(r.TargetSeat)).Select(c =>
                new WumiaoFloodGrainMaterial(c.Id, c.Kind, CardLocation.WoodenOxGrain(r.TargetSeat))).ToArray() : [];
        ClearPendingDecision();
        ReplaceRuntimeTop(use with { WumiaoFlood = r with { Stage = WumiaoFloodStage.DiscardChildren,
            DiscardCardId = cardId, DiscardFrom = from, DiscardPrintedKind = card.Kind,
            DiscardSequenceBefore = before, DiscardSequenceAfter = before, GrainMaterials = grains,
            DiscardNativeDestruction = from.Zone == CardZoneKind.Equipment &&
                (card.IsGeneralWeapon || IsGuanduThunderChariotDeparture(card, from, CardLocation.DiscardPile) || IsDiouCarrier(card.Id)) } });
        MoveLogicalDiscard(r.TargetSeat, r.TargetSeat, card, from, WumiaoFloodDiscardReason, movement =>
        {
            UpdateLifecycleCardUse(use.Id, current => current with
            { WumiaoFlood = current.WumiaoFlood! with { DiscardSequence = movement.Sequence, DiscardTo = movement.To } });
            AdvanceEventRulesAndQueueFact(new WumiaoFloodDiscardPaidEvent(use.Id, r.NullificationFrameId,
                r.TargetSeat, cardId, from, movement.Sequence));
        });
        SealWumiaoFloodDiscardInvoice(use.Id);
        ContinueWumiaoFlood(use.Id);
    }

    private void SealWumiaoFloodDiscardInvoice(long useId)
    {
        var use = LifecycleCardUse(useId)!; var r = use.WumiaoFlood!;
        if (r.DiscardInvoiceSealed || r.DiscardSequence <= r.DiscardSequenceBefore ||
            r.DiscardCardId is null || r.DiscardFrom is null || r.DiscardTo is null || r.DiscardPrintedKind is null)
            throw new InvalidOperationException("Wumiao Flood cannot seal an unissued or repeated native discard invoice.");
        var after = _movementSequence;
        var primary = _cardMovements.Single(m => m.Sequence == r.DiscardSequence);
        var movements = new List<CardMovementRecord> { primary };
        foreach (var grain in r.GrainMaterials)
            movements.Add(_cardMovements.Single(m => m.Sequence > r.DiscardSequence && m.Sequence <= after &&
                m.CardId == grain.CardId && m.CardKind == grain.PrintedKind && m.From == grain.From &&
                m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                (m.To == CardLocation.DiscardPile || IsHellShiyouPaidDiscardMovement(m, grain.From, CardMoveReasons.WoodenOxGrainDiscard))));
        var invoice = movements.OrderBy(m => m.Sequence).Select(m => new WumiaoFloodDiscardMovement(m.CardId,
            m.CardKind, m.From, m.To, m.Reason, m.Sequence)).ToArray();
        var batches = _pendingCardsMovedBatches.Where(b => b.ParentFrameId == use.Id &&
            b.AwaitingProgramFrameId == r.AwaitingProgramFrameId && b.Movements.Count > 0 &&
            b.Movements.All(m => invoice.Any(e => WumiaoFloodDiscardEntryMatches(e, m))))
            .OrderBy(b => b.Id).Select(b => new WumiaoFloodDiscardBatch(b.Id, b.ParentBatchId, b.Movements.Count)).ToArray();
        ReplaceRuntimeFrame(use.Id, use with { WumiaoFlood = r with { DiscardInvoiceSealed = true,
            DiscardSequenceAfter = after, DiscardMovements = invoice, DiscardBatches = batches } });
        AdvanceEventRulesAndQueueFact(new WumiaoFloodDiscardInvoiceSealedEvent(use.Id, r.NullificationFrameId,
            r.DiscardSequence, r.DiscardSequenceBefore, after, r.GrainMaterials.Count, invoice.Length, batches.Length));
        for (var i = 0; i < invoice.Length; i++) { var m = invoice[i]; AdvanceEventRulesAndQueueFact(new WumiaoFloodDiscardMaterialPaidEvent(
            use.Id, r.NullificationFrameId, i, m.CardId, m.PrintedKind, m.From, m.To, m.Reason, m.MovementSequence)); }
        foreach (var b in batches) AdvanceEventRulesAndQueueFact(new WumiaoFloodDiscardBatchIssuedEvent(use.Id,
            r.NullificationFrameId, b.BatchId, b.ParentBatchId, b.MovementCount));
    }
    private static bool WumiaoFloodDiscardEntryMatches(WumiaoFloodDiscardMovement entry, CardMovementRecord movement) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guan_yu_wumiao_WumiaoFlood.WumiaoFloodDiscardEntryMatches(entry,movement);
    private bool IsWumiaoFloodPrimaryDiscardMovement(WumiaoFloodReceipt r, CardMovementRecord movement) =>
        movement.Sequence == r.DiscardSequence && movement.CardId == r.DiscardCardId &&
        movement.CardKind == r.DiscardPrintedKind && movement.From == r.DiscardFrom && movement.To == r.DiscardTo &&
        movement.Reason == WumiaoFloodDiscardReason &&
        (r.DiscardNativeDestruction ? movement.From.Zone == CardZoneKind.Equipment && movement.To == CardLocation.OutsideGame &&
            (GetAdvancedCard(movement.CardId).IsGeneralWeapon ||
             IsGuanduThunderChariotDeparture(GetAdvancedCard(movement.CardId), movement.From, CardLocation.DiscardPile) || IsDiouCarrierDestroyedMaterial(movement)) :
            movement.To == CardLocation.DiscardPile || IsHellShiyouPaidDiscardMovement(movement, movement.From, WumiaoFloodDiscardReason));

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 28510)]
    private bool AdvanceWumiaoFloodDiscardAi()
    {
        if (!HasWumiaoFloodDiscardPrompt() || _players[_pendingDecision!.PlayerSeat].IsHuman) return false;
        ResolveWumiaoFloodDiscard(_pendingDecision.Choices.OrderBy(choice =>
                CardCatalog.Get(GetAdvancedCard(choice.Cards.Single()).Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards.Single()).First());
        AdvanceRulesAndPublishState();
        return true;
    }

    [NativeFrameRoute(typeof(CardUseFrame), 50, typeof(NativeFrameRouteConditions), nameof(NativeFrameRouteConditions.WumiaoFlood))]
    private void ContinueWumiaoFlood(long id)
    {
        while (_resolutionStack.LastOrDefault() is CardUseFrame { WumiaoFlood: { } r } use && use.Id == id)
        {
            if (_pendingDecision is not null) return;
            AssertWumiaoFlood(use);
            if (r.Stage is WumiaoFloodStage.AwaitingDamage or WumiaoFloodStage.FinishingAttack) return;
            if (r.Stage == WumiaoFloodStage.AfterDamage)
            {
                var next = !_players[r.TargetSeat].IsAlive || _winner != Winner.None ? WumiaoFloodStage.FinishingAttack :
                    r.TargetIndex == 0 && WumiaoFloodDiscardable(r.TargetSeat).Count > 0 ? WumiaoFloodStage.ChoosingDiscard :
                    r.TargetIndex == 0 ? WumiaoFloodStage.FinishingAttack : WumiaoFloodStage.Drawing;
                ReplaceRuntimeTop(use with { WumiaoFlood = r with { Stage = next } });
                if (next != WumiaoFloodStage.FinishingAttack) continue;
            }
            else if (r.Stage == WumiaoFloodStage.ChoosingDiscard) { BeginWumiaoFloodDiscard(use); return; }
            else if (r.Stage is WumiaoFloodStage.DiscardChildren or WumiaoFloodStage.DrawingChildren)
            {
                if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.WumiaoFlood) ||
                    TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.WumiaoFlood) ||
                    TryBeginHpChangedProgramWindow(id, PostEventContinuation.WumiaoFlood) ||
                    TryBeginCardsMovedProgramWindow(id)) return;
                use = LifecycleCardUse(id)!;
                ReplaceRuntimeTop(use with { WumiaoFlood = use.WumiaoFlood! with { Stage = WumiaoFloodStage.FinishingAttack } });
            }
            else if (r.Stage == WumiaoFloodStage.Drawing)
            {
                if (use.NativeDraw is null)
                    ReplaceRuntimeTop(use = use with { NativeDraw = new NativeDrawProducerReceipt(0, 1, null,
                        SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw)
                    { Recipients = [r.TargetSeat], ReturnKind = NativeDrawAttemptReturnKind.WumiaoFlood } });
                var paid = use.NativeDraw!;
                if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
                    throw new InvalidOperationException("Wumiao Flood resumed before its actual draw request returned.");
                if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(use)) return;
                var before = OutsidePhaseMovementSequence;
                var ids = paid.Replacement is null && _players[r.TargetSeat].IsAlive && _winner == Winner.None
                    ? DrawCards(_players[r.TargetSeat], 1, true, CardMoveReasons.Draw) : Array.AsReadOnly(Array.Empty<int>());
                var outcome = new NativeDrawOutcome(r.TargetSeat, 1, paid.ActiveAttemptId, before,
                    OutsidePhaseMovementSequence, paid.Replacement) { ActualDrawIds = ids };
                ReplaceRuntimeTop(LifecycleCardUse(id)! with { NativeDraw = null,
                    WumiaoFlood = r with { Stage = WumiaoFloodStage.DrawingChildren, DrawOutcome = outcome } });
                AdvanceEventRulesAndQueueFact(new WumiaoFloodDrawReturnedEvent(id, r.NullificationFrameId,
                    r.TargetSeat, ids.Count, before, outcome.SequenceAfter));
                continue;
            }
            else throw new InvalidOperationException("Wumiao Flood lost its exact effect stage.");
            var attack = new CardAttackHandle(this, id);
            ActiveCardAttack = attack;
            CompleteAttack(attack);
            return;
        }
    }

    private bool IsWumiaoFloodMovement(CardUseFrame use, CardMovementBatchContext batch) =>
        use.WumiaoFlood is { } r && batch.ParentFrameId == use.Id &&
        batch.AwaitingProgramFrameId == r.AwaitingProgramFrameId && batch.Movements.Count > 0 &&
        (r.Stage == WumiaoFloodStage.DrawingChildren && r.DrawOutcome is { } draw && batch.Movements.All(m =>
            m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter &&
            (draw.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile &&
             m.To == CardLocation.Hand(r.TargetSeat) && m.Reason == CardMoveReasons.Draw ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle)) ||
         r.Stage == WumiaoFloodStage.DiscardChildren && r.DiscardInvoiceSealed &&
         r.DiscardBatches.Any(b => b.BatchId == batch.Id && b.ParentBatchId == batch.ParentBatchId && b.MovementCount == batch.Movements.Count) &&
         batch.Movements.All(m => r.DiscardMovements.Any(e => WumiaoFloodDiscardEntryMatches(e, m))));

    private bool IsWumiaoFloodMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame frame) =>
        frame.ResumeWumiaoFloodFrameId == use.Id && frame.Id == frame.Batch.Id && frame.ResumeProgramFrameId is null &&
        !HasAlternateProgramMovementReturn(frame with { ResumeWumiaoFloodFrameId = null }) && IsWumiaoFloodMovement(use, frame.Batch);
    private bool IsWumiaoFloodStateReturn(CardUseFrame use, ProgramLifecycleTriggerWindowFrame frame) =>
        use.WumiaoFlood is { Stage: WumiaoFloodStage.DiscardChildren or WumiaoFloodStage.DrawingChildren } &&
        frame.ResumeProgramFrameId == use.Id && frame.CharacterStateContinuation == CharacterStateContinuation.WumiaoFlood &&
        frame.ResumeCardId is null && frame.ResumeCardKind is null && ProgramEventHistory<CharacterStateChangedEvent>()
            .Count(e => e.Change.Id == frame.Id && e.Change.ParentFrameId == use.Id) == 1;
    private bool IsWumiaoFloodHpReturn(CardUseFrame use, HpChangedTriggerWindowFrame frame) =>
        use.WumiaoFlood is { Stage: WumiaoFloodStage.DiscardChildren or WumiaoFloodStage.DrawingChildren } &&
        frame.Continuation == PostEventContinuation.WumiaoFlood && frame.ResumeFrameId == use.Id &&
        frame.Change.ParentFrameId == use.Id && frame.CardId is null && frame.CardKind is null;
    private bool IsWumiaoFloodRecoveryReturn(CardUseFrame use, RecoveryReplacementFrame frame) =>
        frame.ParentFrameId == use.Id && frame.Return == new RecoveryReplacementReturn(PostEventContinuation.WumiaoFlood, use.Id) &&
        IsWumiaoFloodRecoveryAttempt(use, frame.Attempt);
    private bool IsWumiaoFloodRecoveryAttempt(CardUseFrame use, RecoveryAttempt attempt) =>
        use.WumiaoFlood is { Stage: WumiaoFloodStage.DiscardChildren, DiscardCardId: { } card,
            DiscardFrom: { Zone: CardZoneKind.Equipment } from, DiscardSequence: > 0 } r &&
        from.OwnerSeat == r.TargetSeat && r.DiscardInvoiceSealed && attempt.SourceSeat == r.TargetSeat && attempt.TargetSeat == r.TargetSeat &&
        attempt.Amount == 1 && attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.SilverLion,
            MoveReason: WumiaoFloodDiscardReason) && _cardMovements.Any(m => m.Sequence == r.DiscardSequence &&
            m.CardId == card && m.CardKind == CardKind.SilverLion && m.From == from && IsWumiaoFloodPrimaryDiscardMovement(r, m));

    private bool HasWumiaoFloodProcessing(IReadOnlyList<Card> processing)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().ToArray();
        if (!uses.Any(use => use.WumiaoFlood is not null)) return false;
        foreach (var use in uses) AssertWumiaoFlood(use);
        var ids = uses.SelectMany(use => use.Action?.PhysicalCards.Select(card => card.CardId) ?? []).ToHashSet();
        return processing.All(card => ids.Contains(card.Id));
    }

    private void AssertWumiaoFlood(CardUseFrame use)
    {
        if (use.WumiaoFlood is not { } r) return;
        if (LeChengyanNativeEffectKind(use) != CardKind.WumiaoFlood || use.Action is not { Type: CardActionType.Use } action ||
            action.ActionId != r.ActionId || action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind ||
            r.TargetIndex != use.TargetIndex || r.TargetIndex < 0 || r.TargetIndex >= LeChengyanNativeTargets(use).Count ||
            LeChengyanNativeTargets(use)[r.TargetIndex] != r.TargetSeat || !Enum.IsDefined(r.Stage) || r.ExtraEffectOrdinal < 0 ||
            ProgramEventHistory<WumiaoFloodEffectStartedEvent>().Count(e => e.UseId == use.Id && e.ActionId == r.ActionId &&
                e.NullificationId == r.NullificationFrameId && e.TargetSeat == r.TargetSeat &&
                e.TargetIndex == r.TargetIndex && e.ExtraEffectOrdinal == r.ExtraEffectOrdinal) != 1)
            throw new InvalidOperationException("Wumiao Flood lost its exact action, target cursor or once-only effect.");
        if (r.Stage != WumiaoFloodStage.AwaitingDamage && ProgramEventHistory<WumiaoFloodDamageReturnedEvent>()
            .Count(e => e.UseId == use.Id && e.NullificationId == r.NullificationFrameId && e.TargetSeat == r.TargetSeat &&
                e.TargetIndex == r.TargetIndex) != 1)
            throw new InvalidOperationException("Wumiao Flood lost its completed native damage receipt.");
        if (use.NativeDraw is { } paid && (r.Stage != WumiaoFloodStage.Drawing ||
            paid.ReturnKind != NativeDrawAttemptReturnKind.WumiaoFlood || paid.RequestedCount != 1 ||
            paid.Recipients is not [var recipient] || recipient != r.TargetSeat || paid.RecipientIndex != 0 ||
            paid.InstructionIndex != 0 || paid.ResultBind is not null || paid.Operation is not null || paid.Reason != CardMoveReasons.Draw))
            throw new InvalidOperationException("Wumiao Flood changed its actual once-only draw producer.");
        if (r.Stage == WumiaoFloodStage.DrawingChildren && (r.DrawOutcome is not { } draw || use.NativeDraw is not null ||
            draw.RecipientSeat != r.TargetSeat || draw.RequestedCount != 1 || draw.SequenceBefore > draw.SequenceAfter ||
            draw.SequenceAfter > OutsidePhaseMovementSequence || draw.ActualDrawIds.Count > 1 ||
            draw.Replacement is not null && draw.ActualDrawIds.Count != 0 ||
            draw.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<WumiaoFloodDrawReturnedEvent>().Count(e => e.UseId == use.Id &&
                e.NullificationId == r.NullificationFrameId && e.RecipientSeat == r.TargetSeat &&
                e.ActualCount == draw.ActualDrawIds.Count && e.SequenceBefore == draw.SequenceBefore && e.SequenceAfter == draw.SequenceAfter) != 1))
            throw new InvalidOperationException("Wumiao Flood lost its real draw result and child boundary.");
        if (r.DiscardSequence > 0 && (r.DiscardCardId is null || r.DiscardFrom is null || r.DiscardPrintedKind is null ||
            !r.DiscardInvoiceSealed || !_cardMovements.Any(m => IsWumiaoFloodPrimaryDiscardMovement(r, m)) ||
            ProgramEventHistory<WumiaoFloodDiscardPaidEvent>().Count(e => e.UseId == use.Id &&
                e.NullificationId == r.NullificationFrameId && e.TargetSeat == r.TargetSeat &&
                e.CardId == r.DiscardCardId && e.From == r.DiscardFrom && e.MovementSequence == r.DiscardSequence) != 1))
            throw new InvalidOperationException("Wumiao Flood lost its original target's real discard.");
        if (r.DiscardInvoiceSealed) AssertWumiaoFloodDiscardInvoice(use, r);
    }

    private void AssertWumiaoFloodDiscardInvoice(CardUseFrame use, WumiaoFloodReceipt r)
    {
        var sealedFacts = ProgramEventHistory<WumiaoFloodDiscardInvoiceSealedEvent>().Where(e =>
            e.UseId == use.Id && e.NullificationId == r.NullificationFrameId).ToArray();
        var materials = ProgramEventHistory<WumiaoFloodDiscardMaterialPaidEvent>().Where(e =>
            e.UseId == use.Id && e.NullificationId == r.NullificationFrameId).ToArray();
        var batches = ProgramEventHistory<WumiaoFloodDiscardBatchIssuedEvent>().Where(e =>
            e.UseId == use.Id && e.NullificationId == r.NullificationFrameId).ToArray();
        if (r.Stage is not (WumiaoFloodStage.DiscardChildren or WumiaoFloodStage.FinishingAttack) ||
            r.DiscardFrom != CardLocation.Hand(r.TargetSeat) && r.DiscardFrom != CardLocation.Equipment(r.TargetSeat) ||
            r.DiscardSequenceBefore < 0 || r.DiscardSequence <= r.DiscardSequenceBefore ||
            r.DiscardSequenceAfter < r.DiscardSequence || r.DiscardSequenceAfter > _movementSequence ||
            r.GrainMaterials is not System.Collections.IList { IsReadOnly: true } ||
            r.DiscardMovements is not System.Collections.IList { IsReadOnly: true } ||
            r.DiscardBatches is not System.Collections.IList { IsReadOnly: true } ||
            r.GrainMaterials.Select(g => g.CardId).Distinct().Count() != r.GrainMaterials.Count ||
            r.GrainMaterials.Any(g => g.CardId == r.DiscardCardId || g.From != CardLocation.WoodenOxGrain(r.TargetSeat)) ||
            r.GrainMaterials.Count > 0 && (r.DiscardPrintedKind != CardKind.WoodenOx || r.DiscardFrom != CardLocation.Equipment(r.TargetSeat)) ||
            r.DiscardMovements.Count != r.GrainMaterials.Count + 1 ||
            r.DiscardMovements.Select(m => m.CardId).Distinct().Count() != r.DiscardMovements.Count ||
            !r.DiscardMovements.Select(m => m.MovementSequence).SequenceEqual(r.DiscardMovements.Select(m => m.MovementSequence).Order()) ||
            r.DiscardMovements[0].MovementSequence != r.DiscardSequence ||
            r.DiscardMovements.Any(m => m.MovementSequence <= r.DiscardSequenceBefore || m.MovementSequence > r.DiscardSequenceAfter ||
                _cardMovements.Count(actual => WumiaoFloodDiscardEntryMatches(m, actual)) != 1) ||
            sealedFacts is not [var sealedFact] || sealedFact != new WumiaoFloodDiscardInvoiceSealedEvent(use.Id,
                r.NullificationFrameId, r.DiscardSequence, r.DiscardSequenceBefore, r.DiscardSequenceAfter,
                r.GrainMaterials.Count, r.DiscardMovements.Count, r.DiscardBatches.Count) ||
            materials.Length != r.DiscardMovements.Count || !materials.Select((e, i) => e == new WumiaoFloodDiscardMaterialPaidEvent(
                use.Id, r.NullificationFrameId, i, r.DiscardMovements[i].CardId, r.DiscardMovements[i].PrintedKind,
                r.DiscardMovements[i].From, r.DiscardMovements[i].To, r.DiscardMovements[i].Reason, r.DiscardMovements[i].MovementSequence)).All(v => v) ||
            batches.Length != r.DiscardBatches.Count || r.DiscardBatches.Select(b => b.BatchId).Distinct().Count() != r.DiscardBatches.Count ||
            !batches.Select((e, i) => e == new WumiaoFloodDiscardBatchIssuedEvent(use.Id, r.NullificationFrameId,
                r.DiscardBatches[i].BatchId, r.DiscardBatches[i].ParentBatchId, r.DiscardBatches[i].MovementCount)).All(v => v))
            throw new InvalidOperationException("Wumiao Flood lost its sealed original discard and exact native equipment-child invoice.");
        foreach (var grain in r.GrainMaterials)
            if (r.DiscardMovements.Where(m => m.CardId == grain.CardId && m.PrintedKind == grain.PrintedKind && m.From == grain.From &&
                    m.Reason == CardMoveReasons.WoodenOxGrainDiscard).ToArray() is not [var paid] ||
                !_cardMovements.Any(m => WumiaoFloodDiscardEntryMatches(paid, m) &&
                    (m.To == CardLocation.DiscardPile || IsHellShiyouPaidDiscardMovement(m, grain.From, CardMoveReasons.WoodenOxGrainDiscard))))
                throw new InvalidOperationException("Wumiao Flood's Ox child lost an originally frozen grain or its real native destination.");
    }
}
