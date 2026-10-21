namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly IReadOnlyList<CardKind> GuanduOrdinaryTrickKinds = Array.AsReadOnly(new[]
        { CardKind.StoreGrain, CardKind.Reinforcements, CardKind.UnitedEffort });
    private static bool IsGuanduCard(CardKind kind) => kind == CardKind.Intoxication || IsGuanduTrick(kind);
    private static bool IsGuanduTrick(CardKind kind) => GuanduOrdinaryTrickKinds.Contains(kind);
    private static LegalActionKind GuanduCardAction(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_GuanduCards.GuanduCardAction(kind);
    private bool GuanduIntoxicationTargetSpent(int target) => ProgramEventHistory<GuanduIntoxicationCommittedEvent>()
        .Any(e => e.ActualTurnNumber == _turnNumber && e.TargetSeat == target);
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 220)]
    private int GuanduIntoxicationDamageBonus(IDamageAttempt attack) => !IsGuanduActivity ||
        _phase is TurnPhase.NotStarted or TurnPhase.Finished ? 0 : ProgramEventHistory<GuanduIntoxicationAppliedEvent>()
            .Count(e => e.ActualTurnNumber == _turnNumber && e.TargetSeat == attack.TargetSeat);
    private bool CanBeGuanduAdditionalTarget(CardUseFrame use, CharacterState target) => target.IsAlive && (use.CardKind switch
    {
        CardKind.StoreGrain => true,
        CardKind.Reinforcements => target.Seat != use.SourceSeat && IsRuleWounded(target),
        CardKind.Intoxication => !GuanduAllies(use.SourceSeat, target.Seat) && !GuanduIntoxicationTargetSpent(target.Seat),
        _ => false
    });
    private IReadOnlyList<int> GuanduEligibleTargets(CharacterState actor, CardKind kind, Suit? suit, bool? color,
        bool excludeOwner = false) => Enumerable.Range(0, _playerCount)
        .Select(offset => _players[TableStepSeat(actor.Seat, offset)])
        .Where(target => target.IsAlive && (!excludeOwner || actor.Seat != target.Seat) && (kind switch
        {
            CardKind.Intoxication => !GuanduAllies(actor.Seat, target.Seat) && !GuanduIntoxicationTargetSpent(target.Seat),
            CardKind.Reinforcements => target.Seat != actor.Seat && IsRuleWounded(target),
            CardKind.StoreGrain or CardKind.UnitedEffort => true,
            _ => false
        }) && !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) &&
            !IsCardTargetProhibited(target, kind, suit, color) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, suit) &&
            !IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, kind) &&
            (kind != CardKind.UnitedEffort || !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind)))
        .Select(target => target.Seat).ToArray();
    private IEnumerable<IReadOnlyList<int>> GuanduTargetChoices(CharacterState actor, CardKind kind, Suit? suit,
        bool? color, bool excludeOwner = false)
    {
        var seats = GuanduEligibleTargets(actor, kind, suit, color, excludeOwner);
        if (kind == CardKind.UnitedEffort)
        {
            foreach (var allies in new[] { true, false })
            {
                var team = ApplyTurnCardGroupTargetRestrictions(actor, seats.Where(seat => GuanduAllies(actor.Seat, seat) == allies).ToArray());
                if (team.Count > 0) yield return team;
            }
            yield break;
        }
        var cap = kind == CardKind.StoreGrain ? 3 : kind == CardKind.Reinforcements ? 2 : 1;
        for (var mask = 1; mask < (1 << seats.Count); mask++)
        {
            var selected = seats.Where((_, i) => (mask & (1 << i)) != 0).ToArray();
            if (selected.Length <= cap) yield return Array.AsReadOnly(selected);
        }
    }
    private IReadOnlyList<int>? GuanduExpectedNativeTargets(CharacterState actor, Card card, LegalAction action) =>
        IsGuanduCard(action.PlayedCardKind ?? card.Kind) ? action.TargetSeats : null;
    private void AddGuanduCardActions(List<LegalAction> actions, CharacterState actor, IEnumerable<Card> cards)
    {
        foreach (var card in cards.Where(card => IsGuanduCard(card.Kind)))
        {
            AddGuanduPhysicalOrConvertedActions(actions, actor, card, card.Kind, null);
            if (card.Kind == CardKind.UnitedEffort)
                actions.Add(new(LegalActionKind.Recast, card.Id, null, "重铸【勠力同心】：弃置此牌，摸一张牌"));
        }
    }
    private void AddGuanduPhysicalOrConvertedActions(List<LegalAction> actions, CharacterState actor, Card card,
        CardKind kind, CardConversionSource? conversion)
    {
        if (IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return;
        foreach (var targets in GuanduTargetChoices(actor, kind, EffectiveSuit(actor, card), SuitColor(EffectiveSuit(actor, card))))
            actions.Add(new(GuanduCardAction(kind), card.Id, targets.Count == 1 ? targets[0] : null,
                DescribeConversion(conversion, $"使用【{CardCatalog.Get(kind).DisplayName}】：{string.Join('、', targets.Select(t => _players[t].Name))}"),
                conversion is null ? null : kind, TargetSeats: targets) { ConversionSource = conversion });
    }
    private void ResolveGuanduCard(CharacterState actor, Card card, LegalAction action)
    {
        var kind = action.PlayedCardKind ?? card.Kind;
        if (!IsGuanduCard(kind) || action.TargetSeats.Count == 0)
            throw new InvalidOperationException("A Guandu card lost its published card name or real targets.");
        var targets = Array.AsReadOnly(action.TargetSeats.ToArray());
        var id = BeginCardUse(card, actor.Seat, targets, kind, conversionSource: action.ConversionSource,
            physicalCardIds: card.Id == 0 ? [] : null);
        if (card.Id != 0) MoveCard(card, FindOwnedCardLocation(actor, card), CardLocation.Processing, CardMoveReasons.Use,
            disclosure: CardMovementDisclosure.FaceUp);
        if (kind == CardKind.Intoxication)
        {
            var use = LifecycleCardUse(id)!;
            foreach (var target in targets) AdvanceEventRulesAndQueueFact(new GuanduIntoxicationCommittedEvent(id,
                use.Action!.ActionId, _turnNumber, actor.Seat, target));
            BeginSimpleCardUse(id, new(card.Id, SimpleCardUseEffect.GuanduIntoxication)); return;
        }
        BeginJizhiOrNullificationWindow(id, card, actor.Seat, targets, GuanduCardAction(kind), playedCardKind: kind);
    }
    private bool ContinueGuanduIntoxicationUse(long id, ProgramSimpleCardContinuation continuation)
    {
        if (continuation.Effect != SimpleCardUseEffect.GuanduIntoxication) return false;
        var use = LifecycleCardUse(id) ?? throw new InvalidOperationException("Intoxication lost its native basic use.");
        if (use.CardKind != CardKind.Intoxication || use.Action is not { Type: CardActionType.Use } action ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || use.DyingResponse is not null)
            throw new InvalidOperationException("Intoxication cannot become a rescue, ordinary Wine, or unrelated basic effect.");
        var target = use.TargetSeats[use.TargetIndex];
        if (!ProgramEventHistory<GuanduIntoxicationCommittedEvent>().Any(e => e.UseFrameId == id && e.TargetSeat == target))
            AdvanceEventRulesAndQueueFact(new GuanduIntoxicationCommittedEvent(id, action.ActionId, _turnNumber, use.SourceSeat, target));
        if (!GuanduAllies(use.SourceSeat, target) && _players[target].IsAlive && !IsCardEffectIneffective(id, target))
        {
            var occurrence = ProgramEventHistory<GuanduIntoxicationAppliedEvent>().Count(e => e.UseFrameId == id && e.TargetSeat == target) + 1;
            AdvanceEventRulesAndQueueFact(new GuanduIntoxicationAppliedEvent(id, action.ActionId, _turnNumber, use.SourceSeat, target, occurrence));
        }
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        FinishCardUse(id, GetTrickRepresentation(id, use.CardId, requireProcessing: true), use.CardKind); return true;
    }
    private void ResolveGuanduTrickEffect(NullificationWindowFrame pending)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame use || use.Id != pending.ParentFrameId ||
            !IsGuanduTrick(LeChengyanNativeEffectKind(use)) || pending.EffectCardKind != LeChengyanNativeEffectKind(use) || pending.ActionKind != GuanduCardAction(LeChengyanNativeEffectKind(use)) ||
            use.GuanduCardEffect is not null || use.Action is not { Type: CardActionType.Use } action || use.SourceSeat != pending.SourceSeat ||
            use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count || GetNullificationTargetSeat(pending) is not { } target ||
            LeChengyanNativeTargets(use)[use.TargetIndex] != target)
            throw new InvalidOperationException("A Guandu trick lost its exact resolved native target and effect occurrence.");
        if (!_players[target].IsAlive) { FinishCardUse(use.Id, GetNullificationEffectCard(pending), LeChengyanNativeEffectKind(use)); return; }
        var allied = GuanduAllies(use.SourceSeat, target);
        var amount = LeChengyanNativeEffectKind(use) == CardKind.StoreGrain || LeChengyanNativeEffectKind(use) == CardKind.UnitedEffort && allied && _players[target].IsChained ? 1 : 0;
        var occurrence = ProgramEventHistory<GuanduCardEffectStartedEvent>().Count(e => e.UseFrameId == use.Id && e.TargetIndex == use.TargetIndex) + 1;
        var receipt = new GuanduCardEffectReceipt(action.ActionId, pending.Id, LeChengyanNativeEffectKind(use), target, use.TargetIndex, occurrence,
            LeChengyanNativeEffectKind(use) == CardKind.Reinforcements ? GuanduCardEffectStage.RecoveryChildren :
            LeChengyanNativeEffectKind(use) == CardKind.UnitedEffort && !allied ? GuanduCardEffectStage.ChainChildren : GuanduCardEffectStage.Drawing)
        { AlliedBranch = allied, RequestedCount = amount, AwaitingProgramFrameId = _resolutionStack.OfType<ProgramSkillFrame>()
            .LastOrDefault() is { } parent && IsAwaitingProgramMovement(parent) ? parent.Id : null };
        ReplaceRuntimeTop(use with { Step = ResolutionFrameStep.ResolvingEffect, GuanduCardEffect = receipt });
        AdvanceEventRulesAndQueueFact(new GuanduCardEffectStartedEvent(use.Id, action.ActionId, pending.Id, LeChengyanNativeEffectKind(use),
            target, use.TargetIndex, occurrence, allied, amount));
        if (LeChengyanNativeEffectKind(use) == CardKind.Reinforcements) ApplyGuanduRecovery(use.Id);
        else if (LeChengyanNativeEffectKind(use) == CardKind.UnitedEffort && !allied)
        {
            _players[target].IsChained = ResolveEnteringChain(_players[target], true);
            AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(use.Id, use.SourceSeat, target, _players[target].IsChained));
        }
        ContinueGuanduCardEffect(use.Id);
    }
    private void ApplyGuanduRecovery(long id)
    {
        var use = LifecycleCardUse(id)!; var r = use.GuanduCardEffect!; var target = _players[r.TargetSeat];
        var amount = target.IsAlive && IsRuleWounded(target) ? ApplyIssuedCardRecoveryModifiers(id, use.SourceSeat, target.Seat, 1) : 0;
        UpdateLifecycleCardUse(id, current => current with { GuanduCardEffect = current.GuanduCardEffect! with { RecoveryAmount = amount } });
        AdvanceEventRulesAndQueueFact(new GuanduCardRecoveryIssuedEvent(id, r.NullificationFrameId, r.Occurrence, use.SourceSeat, target.Seat, amount));
        if (amount <= 0 || TryQueueRecoveryReplacement(id, use.SourceSeat, target.Seat, amount,
            new(RecoveryAttemptProducer.CardUse, CardId: use.CardId, CardKind: CardKind.Reinforcements, Policies: []))) return;
        var recoveryId = BeginRecovery(id, use.SourceSeat, target.Seat, amount);
        try
        {
            var actual = Math.Min(amount, target.MaxHp - target.Hp); target.Hp += actual;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(use.SourceSeat, target.Seat, actual, target.Hp));
        }
        finally { PopResolutionFrame(recoveryId, ResolutionFrameKind.Recovery); }
    }
    [NativeFrameRoute(typeof(CardUseFrame), 40, typeof(NativeFrameRouteConditions), nameof(NativeFrameRouteConditions.GuanduCardEffect))]
    private void ContinueGuanduCardEffect(long id)
    {
        while (_resolutionStack.LastOrDefault() is CardUseFrame { GuanduCardEffect: { } r } use && use.Id == id)
        {
            if (_pendingDecision is not null) return;
            if (r.Stage != GuanduCardEffectStage.Drawing)
            {
                if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.GuanduCardEffect) ||
                    TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.GuanduCardEffect) ||
                    TryBeginHpChangedProgramWindow(id, PostEventContinuation.GuanduCardEffect) || TryBeginCardsMovedProgramWindow(id)) return;
                UpdateLifecycleCardUse(id, current => current with { GuanduCardEffect = null, NativeDraw = null });
                FinishCardUse(id, GetTrickRepresentation(id, use.CardId, requireProcessing: true), LeChengyanNativeEffectKind(use)); return;
            }
            if (use.NativeDraw is null)
                ReplaceRuntimeTop(use = use with { NativeDraw = new NativeDrawProducerReceipt(0, r.RequestedCount, null,
                    SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw)
                { Recipients = [r.TargetSeat], ReturnKind = NativeDrawAttemptReturnKind.GuanduCardEffect } });
            var paid = use.NativeDraw!;
            if (paid.ActiveAttemptId is not null && !paid.AttemptReturned) throw new InvalidOperationException("A Guandu draw resumed before its actual request returned.");
            if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(use)) return;
            var before = OutsidePhaseMovementSequence;
            var ids = paid.Replacement is null && _players[r.TargetSeat].IsAlive && _winner == Winner.None
                ? DrawCards(_players[r.TargetSeat], r.RequestedCount, true, CardMoveReasons.Draw) : Array.AsReadOnly(Array.Empty<int>());
            var outcome = new NativeDrawOutcome(r.TargetSeat, r.RequestedCount, paid.ActiveAttemptId, before,
                OutsidePhaseMovementSequence, paid.Replacement) { ActualDrawIds = ids };
            UpdateLifecycleCardUse(id, current => current with { NativeDraw = null, GuanduCardEffect = r with
                { Stage = GuanduCardEffectStage.DrawingChildren, DrawOutcome = outcome } });
            AdvanceEventRulesAndQueueFact(new GuanduCardDrawAppliedEvent(id, r.NullificationFrameId, r.Occurrence, r.TargetSeat,
                r.RequestedCount, ids.Count, before, outcome.SequenceAfter));
        }
    }
    private bool IsGuanduCardEffectMovement(CardUseFrame use, CardMovementBatchContext batch) =>
        use.GuanduCardEffect is { Stage: GuanduCardEffectStage.DrawingChildren, DrawOutcome: { } result } r &&
        batch.ParentFrameId == use.Id && batch.AwaitingProgramFrameId == r.AwaitingProgramFrameId && batch.Movements.Count > 0 &&
        batch.Movements.All(m => m.Sequence > result.SequenceBefore && m.Sequence <= result.SequenceAfter &&
            (result.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(result.RecipientSeat) &&
                m.Reason == CardMoveReasons.Draw || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle));
    private bool IsGuanduCardEffectMovementReturn(CardUseFrame use, CardsMovedTriggerWindowFrame frame) =>
        frame.ResumeGuanduCardEffectFrameId == use.Id && frame.Id == frame.Batch.Id && frame.ResumeProgramFrameId is null &&
        !HasAlternateProgramMovementReturn(frame with { ResumeGuanduCardEffectFrameId = null }) && IsGuanduCardEffectMovement(use, frame.Batch);
    private bool IsGuanduCardEffectStateReturn(CardUseFrame use, ProgramLifecycleTriggerWindowFrame frame) =>
        use.GuanduCardEffect is { Stage: GuanduCardEffectStage.DrawingChildren or GuanduCardEffectStage.RecoveryChildren or GuanduCardEffectStage.ChainChildren } &&
        frame.ResumeProgramFrameId == use.Id && frame.CharacterStateContinuation == CharacterStateContinuation.GuanduCardEffect &&
        frame.ResumeCardId is null && frame.ResumeCardKind is null &&
        ProgramEventHistory<CharacterStateChangedEvent>().Count(e => e.Change.Id == frame.Id && e.Change.ParentFrameId == use.Id) == 1;
    private bool IsGuanduCardEffectHpReturn(CardUseFrame use, HpChangedTriggerWindowFrame frame) =>
        use.GuanduCardEffect is { Stage: GuanduCardEffectStage.RecoveryChildren } r && frame.ResumeFrameId == use.Id &&
        frame.Continuation == PostEventContinuation.GuanduCardEffect && frame.Change.ParentFrameId == use.Id &&
        frame.Change.SourceSeat == use.SourceSeat && frame.Change.TargetSeat == r.TargetSeat && frame.CardId is null && frame.CardKind is null;
    private bool IsGuanduCardRecoveryAttempt(CardUseFrame use, RecoveryAttempt attempt) =>
        use.GuanduCardEffect is { Stage: GuanduCardEffectStage.RecoveryChildren, RecoveryAmount: > 0 } r &&
        attempt.SourceSeat == use.SourceSeat && attempt.TargetSeat == r.TargetSeat && attempt.Amount == r.RecoveryAmount &&
        attempt.Completion.Producer == RecoveryAttemptProducer.CardUse && attempt.Completion.CardId == use.CardId &&
        attempt.Completion.CardKind == LeChengyanNativeEffectKind(use) && attempt.Completion.Policies is { Count: 0 } &&
        ProgramEventHistory<GuanduCardRecoveryIssuedEvent>().Count(e => e.UseFrameId == use.Id && e.NullificationFrameId == r.NullificationFrameId &&
            e.Occurrence == r.Occurrence && e.SourceSeat == use.SourceSeat && e.TargetSeat == r.TargetSeat && e.Amount == attempt.Amount) == 1;
    private bool IsGuanduCardEffectRecoveryReturn(CardUseFrame use, RecoveryReplacementFrame frame) =>
        frame.ParentFrameId == use.Id && frame.Return == new RecoveryReplacementReturn(PostEventContinuation.GuanduCardEffect, use.Id) &&
        IsGuanduCardRecoveryAttempt(use, frame.Attempt);
    private bool HasGuanduCardEffectProcessing(IReadOnlyList<Card> processing)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().ToArray();
        if (!uses.Any(use => use.GuanduCardEffect is not null)) return false;
        foreach (var use in uses) AssertGuanduCardReceipt(use);
        var materials = uses.SelectMany(use => use.Action?.PhysicalCards.Select(card => card.CardId) ?? []).ToHashSet();
        return processing.All(card => materials.Contains(card.Id));
    }
    private void AssertGuanduCardReceipt(CardUseFrame use)
    {
        if (use.GuanduCardEffect is not { } r) return;
        if (!IsGuanduTrick(LeChengyanNativeEffectKind(use)) || r.Kind != LeChengyanNativeEffectKind(use) || use.Action?.ActionId != r.ActionId ||
            r.TargetIndex != use.TargetIndex || r.TargetIndex < 0 || r.TargetIndex >= LeChengyanNativeTargets(use).Count ||
            LeChengyanNativeTargets(use)[r.TargetIndex] != r.TargetSeat || r.Occurrence < 1 || !Enum.IsDefined(r.Stage) || r.RequestedCount is < 0 or > 1 ||
            ProgramEventHistory<GuanduCardEffectStartedEvent>().Count(e => e.UseFrameId == use.Id && e.ActionId == r.ActionId &&
                e.NullificationFrameId == r.NullificationFrameId && e.Kind == LeChengyanNativeEffectKind(use) && e.TargetIndex == r.TargetIndex &&
                e.TargetSeat == r.TargetSeat && e.Occurrence == r.Occurrence && e.AlliedBranch == r.AlliedBranch && e.RequestedCount == r.RequestedCount) != 1)
            throw new InvalidOperationException("A Guandu effect lost its exact native use, target cursor, or once-issued effect occurrence.");
        if (use.NativeDraw is { } paid && (r.Stage != GuanduCardEffectStage.Drawing || paid.ReturnKind != NativeDrawAttemptReturnKind.GuanduCardEffect ||
            paid.RequestedCount != r.RequestedCount || paid.Recipients is not [var seat] || seat != r.TargetSeat || paid.RecipientIndex != 0 ||
            paid.InstructionIndex != 0 || paid.ResultBind is not null || paid.Operation is not null || paid.Reason != CardMoveReasons.Draw ||
            paid.ActiveAttemptId is null && paid.AttemptReturned))
            throw new InvalidOperationException("A Guandu effect changed its already issued native draw request.");
        if (r.Stage == GuanduCardEffectStage.DrawingChildren && (r.DrawOutcome is not { } result || use.NativeDraw is not null ||
            result.RecipientSeat != r.TargetSeat || result.RequestedCount != r.RequestedCount || result.SequenceBefore > result.SequenceAfter ||
            result.SequenceAfter > OutsidePhaseMovementSequence || result.ActualDrawIds.Count > r.RequestedCount ||
            result.ActualDrawIds.Distinct().Count() != result.ActualDrawIds.Count || result.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
            result.Replacement is not null && result.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > result.SequenceBefore && m.Sequence <= result.SequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(result.RecipientSeat) && m.Reason == CardMoveReasons.Draw).OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(result.ActualDrawIds) ||
            ProgramEventHistory<GuanduCardDrawAppliedEvent>().Count(e => e.UseFrameId == use.Id && e.NullificationFrameId == r.NullificationFrameId &&
                e.Occurrence == r.Occurrence && e.RecipientSeat == result.RecipientSeat && e.RequestedCount == r.RequestedCount &&
                e.ActualDrawCount == result.ActualDrawIds.Count && e.SequenceBefore == result.SequenceBefore && e.SequenceAfter == result.SequenceAfter) != 1))
            throw new InvalidOperationException("A Guandu draw lost its actual immutable materials and native completion receipt.");
    }
    private void AssertGuanduCardState()
    { foreach (var use in _resolutionStack.OfType<CardUseFrame>()) AssertGuanduCardReceipt(use); }
}
