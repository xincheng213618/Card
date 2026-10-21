namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginOriginalUseLightning(long useId, CardConversionSource source, string gameplayHash)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Lightning lost its native Use.");
        if (_resolutionStack.LastOrDefault()?.Id != useId || use.OriginalUseLightning is not null ||
            use.CardId != 0 || use.CardKind != CardKind.Lightning || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, PhysicalCards.Count: 0 } action ||
            action.ActorSeat != use.SourceSeat || action.ProviderSeat != action.ActorSeat ||
            action.RequesterSeat is not null || action.ResponderSeat is not null ||
            action.EffectiveKind != CardKind.Lightning || action.EffectiveSuit != Suit.None ||
            action.EffectiveRank != 0 || action.EffectiveIsRed is not null ||
            !action.ConversionChain.SequenceEqual([source]) || source.OwnerSeat != use.SourceSeat ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || use.TargetSeats.Count == 0 ||
            use.TargetSeats.Distinct().Count() != use.TargetSeats.Count || use.TargetSeats.Any(s => !IsValidPlayerSeat(s)) ||
            _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program?.GameplayHash != gameplayHash)
            throw new InvalidOperationException("Held Lightning requires its exact accepted empty-material native Use and source.");
        var origin = new OriginalUseLightningOrigin(use.Id, action.ActionId, source, gameplayHash);
        UpdateLifecycleCardUse(use.Id, f => f with { OriginalUseLightning = origin });
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningIssuedEvent(origin));
        BeginJizhiOrNullificationWindow(use.Id, new(0, CardKind.Lightning, Suit.None, 0),
            use.SourceSeat, use.TargetSeats, LegalActionKind.Lightning, playedCardKind: CardKind.Lightning);
    }

    private bool IsOriginalUseLightning(long id) => LifecycleCardUse(id) is
        { OriginalUseLightning: { } origin, CardId: 0, CardKind: CardKind.Lightning, PhysicalCardIds.Count: 0,
          Action: { Type: CardActionType.Use, PhysicalCards.Count: 0 } action } use &&
        origin.UseId == id && origin.ActionId == action.ActionId && action.EffectiveKind == CardKind.Lightning &&
        action.ActorSeat == use.SourceSeat && action.ProviderSeat == use.SourceSeat &&
        action.EffectiveSuit == Suit.None && action.EffectiveRank == 0 && action.EffectiveIsRed is null &&
        action.RequesterSeat is null && action.ResponderSeat is null &&
        origin.Source.OwnerSeat == use.SourceSeat && action.ConversionChain.SequenceEqual([origin.Source]) &&
        action.TargetSeats.SequenceEqual(use.TargetSeats) &&
        ProgramEventHistory<OriginalUseLightningIssuedEvent>().Count(e => e.Origin == origin) == 1;

    [NativeUseValidation(37450)]
    private void ValidateOriginalUseLightning(CardUseFrame use)
    {
        if (use.OriginalUseLightning is not null && !IsOriginalUseLightning(use.Id))
            throw new InvalidOperationException("Lightning lost its original empty-material native Use.");
    }

    private bool MatchesOriginalUseLightningAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && IsOriginalUseLightning(id) && LifecycleCardUse(id)!.Action is { } issued &&
        action.ActionId == issued.ActionId && action.Type == issued.Type &&
        action.ActorSeat == issued.ActorSeat && action.ProviderSeat == issued.ProviderSeat &&
        action.RequesterSeat == issued.RequesterSeat && action.ResponderSeat == issued.ResponderSeat &&
        action.EffectiveKind == issued.EffectiveKind && action.EffectiveSuit == issued.EffectiveSuit &&
        action.EffectiveRank == issued.EffectiveRank && action.EffectiveIsRed == issued.EffectiveIsRed &&
        action.PhysicalCards.Count == 0 && action.ConversionChain.SequenceEqual(issued.ConversionChain) &&
        action.TargetSeats.SequenceEqual(issued.TargetSeats);

    private bool BeginOriginalUseLightningFinalized(long id, Card card, LegalActionKind actionKind)
    {
        if (!IsOriginalUseLightning(id) || card.Id != 0 || actionKind != LegalActionKind.Lightning)
            throw new InvalidOperationException("Lightning finalized targets lost their original zero-material Use.");
        var use = LifecycleCardUse(id)!;
        if (use.FinalizedTrickProgramsStarted) return false;
        TryMarkProgramUseAccepted(id);
        if (!TryMarkFinalizedTrickProgramsStarted(id)) return false;
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(use.Action!));
        return TryBeginProgramCardWindow(null, use.Action!, SkillProgramTriggerWindow.CardUseTargetsFinalized,
            use.TargetSeats, ProgramCardContinuation.FinalizedDelayedPlacement,
            trickContinuation: new(0, LegalActionKind.Lightning));
    }

    private bool IsOriginalUseLightningFinalizedWindow(ProgramCardTriggerWindowFrame window) =>
        window.Continuation == ProgramCardContinuation.FinalizedDelayedPlacement &&
        window.TrickContinuation is { EffectCardId: 0, ActionKind: LegalActionKind.Lightning,
            TargetCardId: null, RequiredCardKind: null } &&
        window.SimpleContinuation is null && window.CompletedResponseReturn is null && window.AttackOwnerFrameId is null &&
        IsOriginalUseLightning(window.ParentFrameId) &&
        LifecycleCardUse(window.ParentFrameId) is { FinalizedTrickProgramsStarted: true, ProgramUseAccepted: true } &&
        MatchesOriginalUseLightningAction(window.ParentFrameId, window.Action, 0);

    private void ContinueOriginalUseLightningFinalized(ProgramCardTriggerWindowFrame window)
    {
        if (!IsOriginalUseLightningFinalizedWindow(window) || _resolutionStack.LastOrDefault()?.Id != window.ParentFrameId)
            throw new InvalidOperationException("Lightning finalized targets lost their typed native parent.");
        var use = LifecycleCardUse(window.ParentFrameId)!;
        BeginJizhiOrNullificationWindow(use.Id, new(0, CardKind.Lightning, Suit.None, 0),
            use.SourceSeat, use.TargetSeats, LegalActionKind.Lightning, playedCardKind: CardKind.Lightning);
    }

    private IReadOnlyList<int> OriginalUseLightningPlacementTargets(JizhiResolution pending) =>
        IsOriginalUseLightning(pending.ResolutionId)
            ? LifecycleCardUse(pending.ResolutionId)!.TargetSeats.Skip(LifecycleCardUse(pending.ResolutionId)!.TargetIndex).Take(1).ToArray()
            : pending.TargetSeats;

    private bool PlaceOriginalUseLightning(NullificationWindowFrame pending)
    {
        if (!IsOriginalUseLightning(pending.ParentFrameId)) return false;
        var use = LifecycleCardUse(pending.ParentFrameId)!;
        if (pending.EffectCardId != 0 || pending.EffectCardKind != CardKind.Lightning ||
            pending.ActionKind != LegalActionKind.Lightning || pending.TargetSeats is not [var seat] ||
            use.TargetIndex < 0 || use.TargetIndex >= use.TargetSeats.Count || seat != use.TargetSeats[use.TargetIndex])
            throw new InvalidOperationException("Lightning placement changed its original target cursor.");
        var target = _players[seat];
        if (!target.IsAlive || target.JudgmentAreaAbolished || HasJudgmentEffectiveCard(target, CardKind.Lightning) ||
            HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) ||
            use.IneffectiveTargetSeats?.Contains(seat) == true)
        {
            MarkCardEffectIneffective(use.Id, seat);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, use.SourceSeat, seat,
                CardKind.Lightning, CardEffectSkipReason.PublicTargetMissing));
        }
        else
        {
            AttachOriginalUseLightning(use, use.OriginalUseLightning!.Source, use.OriginalUseLightning.GameplayHash, seat);
            if (!IsExtraCardEffectRound(use.Id))
                AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Lightning, use.SourceSeat, seat));
            AddLog("CardEffect", $"{_players[use.SourceSeat].Name} 对 {target.Name} 使用【闪电】，留下延时效果。",
                use.SourceSeat, seat);
        }
        FinishCardUse(use.Id, new(0, CardKind.Lightning, Suit.None, 0), CardKind.Lightning);
        return true;
    }

    private OriginalUseLightningAttachment AttachOriginalUseLightning(CardUseFrame use,
        CardConversionSource source, string gameplayHash, int target)
    {
        if (!IsOriginalUseLightning(use.Id) || use.OriginalUseLightning is not { } origin ||
            origin.Source != source || origin.GameplayHash != gameplayHash || !use.TargetSeats.Contains(target) ||
            HasJudgmentEffectiveCard(_players[target], CardKind.Lightning) ||
            ProgramEventHistory<OriginalUseLightningPlacedEvent>().Any(e => e.Attachment.Origin == origin && e.Attachment.OriginalTarget == target))
            throw new InvalidOperationException("Lightning cannot replace or duplicate its original Use placement.");
        var id = ++_resolutionSequence;
        var attachment = new OriginalUseLightningAttachment(id, origin, target, target, _movementSequence, id, _turnNumber);
        _players[target].HeldOriginalUseLightning = _players[target].HeldOriginalUseLightning.Append(attachment).ToArray();
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningPlacedEvent(attachment));
        return attachment;
    }

    private bool TryContinueOriginalUseLightningPlacement(long id)
    {
        if (!IsOriginalUseLightning(id)) return false;
        var use = LifecycleCardUse(id)!;
        if (_winner != Winner.None || use.TargetIndex + 1 >= use.TargetSeats.Count) return false;
        UpdateLifecycleCardUse(id, f => f with { TargetIndex = f.TargetIndex + 1 });
        ContinueJizhiOrNullificationAfterTargetTriggers(new(use.SourceSeat, id,
            new(0, CardKind.Lightning, Suit.None, 0), CardKind.Lightning, use.TargetSeats, LegalActionKind.Lightning, null, null));
        return true;
    }

    private bool ValidOriginalUseLightning(OriginalUseLightningAttachment a) =>
        a.Id > 0 && a.Origin.UseId > 0 && a.Origin.ActionId > 0 && IsValidPlayerSeat(a.Origin.Source.OwnerSeat) &&
        IsValidPlayerSeat(a.OriginalTarget) && IsValidPlayerSeat(a.Holder) && a.AfterMovement >= 0 && a.Order > 0 &&
        ProgramEventHistory<OriginalUseLightningIssuedEvent>().Count(e => e.Origin == a.Origin) == 1 &&
        ProgramEventHistory<OriginalUseLightningPlacedEvent>().Count(e => e.Attachment.Id == a.Id &&
            e.Attachment.Origin == a.Origin && e.Attachment.OriginalTarget == a.OriginalTarget &&
            e.Attachment.PlacedTurn == a.PlacedTurn) == 1;

    private OriginalUseLightningAttachment? HeldOriginalUseLightning(long id) =>
        _players.SelectMany(p => p.HeldOriginalUseLightning).SingleOrDefault(a => a.Id == id);

    private bool ExactOriginalUseLightning(OriginalUseLightningAttachment a) =>
        ValidOriginalUseLightning(a) && HeldOriginalUseLightning(a.Id) == a;

    private bool HasHeldOriginalUseLightning(CharacterState player) =>
        player.HeldOriginalUseLightning.Any(ValidOriginalUseLightning);

    private IReadOnlyList<PlayerMarkerSnapshot>? AppendOriginalUseLightningMarker(int seat, IReadOnlyList<PlayerMarkerSnapshot>? markers) =>
        HasHeldOriginalUseLightning(_players[seat])
            ? Array.AsReadOnly((markers ?? []).Append(new PlayerMarkerSnapshot(PlayerMarkerKind.OriginalUseLightning,
                "闪电", _players[seat].HeldOriginalUseLightning.Count)).ToArray()) : markers;

    private bool TryBeginHeldOriginalUseLightning(CharacterState current, int? excludedCard)
    {
        var attachment = current.HeldOriginalUseLightning
            .Where(a => a.LastJudgmentTurn != _turnNumber && ValidOriginalUseLightning(a))
            .OrderBy(a => a.AfterMovement).ThenBy(a => a.Order).FirstOrDefault();
        if (attachment is null) return false;
        var physical = GetJudgment(current).FirstOrDefault(c => !IsZixiQinJudgmentCard(c.Id) &&
            IsDelayedCard(GetJudgmentEffectiveCardKind(c)) && c.Id != excludedCard);
        if (physical is not null && (_cardMovements.LastOrDefault(m => m.CardId == physical.Id)?.Sequence ?? 0) <= attachment.AfterMovement)
            return false;
        if (!current.IsAlive || current.JudgmentAreaAbolished || _turnNumber < 1 ||
            _phase != TurnPhase.Draw || _currentSeat != current.Seat)
            throw new InvalidOperationException("Held Lightning requires its holder's actual judgment boundary.");
        attachment = attachment with { LastJudgmentTurn = _turnNumber };
        ReplaceHeldOriginalUseLightning(attachment);
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningVisitedEvent(attachment.Id, current.Seat, _turnNumber));
        var parent = _resolutionStack.LastOrDefault();
        var delayed = new DelayedEffectContinuation(0, CardKind.Lightning, current.Seat, _turnNumber, _phase,
            parent?.Id ?? 0, parent?.Kind, parent?.Step)
            { OriginalUseLightning = attachment, OriginalUseLightningExcludedCardId = excludedCard };
        var window = new NullificationWindowFrame(++_resolutionSequence, delayed.ParentFrameId, current.Seat,
            [current.Seat], 0, CardKind.Lightning, LegalActionKind.Lightning, null, null,
            BuildNullificationCandidateSeats(current.Seat)) { DelayedReturn = delayed };
        PushRuntimeFrame(window);
        ContinueNullificationWindow(window);
        return true;
    }

    private bool ValidOriginalUseLightningDelayed(DelayedEffectContinuation d) =>
        d.OriginalUseLightning is { } a && ExactOriginalUseLightning(a) && d.CardId == 0 &&
        d.EffectiveKind == CardKind.Lightning && d.OwnerSeat == a.Holder &&
        d.TurnNumber == _turnNumber && d.Phase == _phase && _currentSeat == a.Holder;

    private void CompleteOriginalUseLightningNullification(DelayedEffectContinuation delayed, bool nullified, long windowId)
    {
        if (!ValidOriginalUseLightningDelayed(delayed))
            throw new InvalidOperationException("Held Lightning lost its exact counterspell return.");
        var a = delayed.OriginalUseLightning!;
        if (SkipUnavailableOriginalUseLightning(a, delayed.OriginalUseLightningExcludedCardId)) return;
        if (nullified)
        {
            TransferOriginalUseLightning(a, windowId, null, delayed.OriginalUseLightningExcludedCardId);
            return;
        }
        if (HasCardEffectBeforeApplyPrograms)
        {
            var action = new CardActionContext(0, null, CardActionType.Use, a.Holder, a.Holder, null, null, null,
                CardKind.Lightning, [a.Holder], [], [], [a.Holder]);
            if (BeginCardEffectPrograms(action, [a.Holder], null, delayed)) return;
        }
        BeginOriginalUseLightningJudgment(delayed);
    }

    private void BeginOriginalUseLightningJudgment(DelayedEffectContinuation delayed)
    {
        if (!ValidOriginalUseLightningDelayed(delayed))
            throw new InvalidOperationException("Held Lightning lost its exact effect return.");
        var a = delayed.OriginalUseLightning!;
        if (SkipUnavailableOriginalUseLightning(a, delayed.OriginalUseLightningExcludedCardId)) return;
        var result = BeginJudgment(null, a.Holder, JudgmentReasons.Lightning, 0, CardKind.Lightning,
            JudgmentContinuationKind.Lightning, sourceSeat: a.Holder, originalUseLightning: a,
            originalUseLightningExcludedCardId: delayed.OriginalUseLightningExcludedCardId);
        if (result is null) AdvanceRulesAndPublishState();
    }

    private bool IsOriginalUseLightningJudgment(JudgmentFrame j) =>
        j.OriginalUseLightning is { } a && ValidOriginalUseLightning(a) &&
        j.TargetSeat == a.Holder && j.SourceSeat == a.Holder && j.ParentFrameId == 0 &&
        j.ParentAttackId is null && j.DelayedCardId is null && j.Reason == JudgmentReasons.Lightning &&
        j.Continuation == JudgmentContinuationKind.Lightning &&
        ProgramEventHistory<JudgmentRequestedEvent>().Count(e => e.ResolutionId == j.Id &&
            e.ParentResolutionId == 0 && e.TargetSeat == a.Holder && e.Reason == JudgmentReasons.Lightning &&
            e.SourceCard == CardKind.Lightning) == 1;

    private void CompleteOriginalUseLightningJudgment(JudgmentFrame pending, bool hit)
    {
        var judgment = _resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j => j.Id == pending.Id) ?? pending;
        if (!IsOriginalUseLightningJudgment(judgment) || judgment.OriginalUseLightning is not { } a || !ExactOriginalUseLightning(a))
            throw new InvalidOperationException("Lightning judgment lost its original held effect.");
        if (!hit)
        {
            TransferOriginalUseLightning(a, judgment.Id, judgment.CardId, judgment.OriginalUseLightningExcludedCardId);
            return;
        }
        if (judgment.Succeeded != true || _resolutionStack.LastOrDefault()?.Id != judgment.Id)
            throw new InvalidOperationException("Only the actual finalized Lightning hit can begin native damage.");
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningResultEvent(a.Id, judgment.Id, a.Holder, true, judgment.CardId, null));
        var attack = new CardAttackHandle(this, judgment.Id, a.Holder, a.Holder, card: null, damageAmount: 3,
            playedCardKind: CardKind.Lightning, isDelayedJudgmentDamage: true, delayedJudgmentSeat: a.Holder);
        ActiveCardAttack = attack;
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }

    private bool IsOriginalUseLightningAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        attack.IsDelayedJudgmentDamage && attack.IsSourceLess && attack.Card is null &&
        attack.PhysicalCards.Count == 0 && processing.Count == 0 && attack.EffectiveCardKind == CardKind.Lightning &&
        _resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j => j.Id == attack.ResolutionId) is
            { OriginalUseLightning: { } a } judgment && IsOriginalUseLightningJudgment(judgment) &&
        attack.SourceSeat == a.Holder && attack.DelayedJudgmentSeat == a.Holder;

    private bool FinishOriginalUseLightningAttack(CardAttackHandle attack)
    {
        if (_resolutionStack.OfType<JudgmentFrame>().SingleOrDefault(j => j.Id == attack.ResolutionId) is not
            { OriginalUseLightning: { } a } judgment) return false;
        if (!attack.IsDelayedJudgmentDamage || !attack.IsSourceLess || !IsOriginalUseLightningJudgment(judgment))
            throw new InvalidOperationException("Only the attachment's native delayed attack can consume it.");
        RemoveOriginalUseLightning(a, "resolved");
        if (_winner != Winner.None && _status != EngineStatus.Completed) CompleteGame();
        return true;
    }

    private void TransferOriginalUseLightning(OriginalUseLightningAttachment a, long judgmentId, int? cardId, int? excludedCard)
    {
        if (!ExactOriginalUseLightning(a)) throw new InvalidOperationException("Lightning transfer lost its original held effect.");
        var holder = a.Holder;
        var nextHolder = Enumerable.Range(1, _playerCount - 1).Select(offset => TableStepSeat(holder, offset))
            .FirstOrDefault(seat => _players[seat].IsAlive && !_players[seat].JudgmentAreaAbolished &&
                !HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget) &&
                !IsBlackTrickTargetProhibited(_players[seat], CardKind.Lightning, null) &&
                !HasJudgmentEffectiveCard(_players[seat], CardKind.Lightning), holder);
        _players[holder].HeldOriginalUseLightning = _players[holder].HeldOriginalUseLightning.Where(x => x.Id != a.Id).ToArray();
        var next = a with { Holder = nextHolder, AfterMovement = _movementSequence, Order = ++_resolutionSequence,
            LastJudgmentTurn = nextHolder == holder ? _turnNumber : -1 };
        _players[nextHolder].HeldOriginalUseLightning = _players[nextHolder].HeldOriginalUseLightning.Append(next).ToArray();
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningTransferredEvent(a.Id, holder, nextHolder, next.AfterMovement, next.Order));
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningResultEvent(a.Id, judgmentId, holder, false, cardId, nextHolder));
        if (_winner == Winner.None) ResumeAfterLightningDamage(holder, excludedCard);
    }

    private void ReplaceHeldOriginalUseLightning(OriginalUseLightningAttachment a) =>
        _players[a.Holder].HeldOriginalUseLightning = _players[a.Holder].HeldOriginalUseLightning.Select(x => x.Id == a.Id ? a : x).ToArray();

    private void RemoveOriginalUseLightning(OriginalUseLightningAttachment a, string reason)
    {
        if (!ExactOriginalUseLightning(a)) throw new InvalidOperationException("Lightning removal lost its exact held identity.");
        _players[a.Holder].HeldOriginalUseLightning = _players[a.Holder].HeldOriginalUseLightning.Where(x => x.Id != a.Id).ToArray();
        AdvanceEventRulesAndQueueFact(new OriginalUseLightningRemovedEvent(a.Id, a.Holder, reason));
    }

    private bool SkipUnavailableOriginalUseLightning(OriginalUseLightningAttachment a, int? excludedCard)
    {
        if (_winner == Winner.None && _players[a.Holder].IsAlive && !_players[a.Holder].JudgmentAreaAbolished) return false;
        RemoveOriginalUseLightning(a, "holder-unavailable");
        if (_winner == Winner.None) ResumeAfterLightningDamage(a.Holder, excludedCard);
        return true;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 37450)]
    private void ClearUnavailableOriginalUseLightning()
    {
        foreach (var player in _players.Where(p => !p.IsAlive || p.JudgmentAreaAbolished))
        foreach (var a in player.HeldOriginalUseLightning.ToArray())
        {
            if (_resolutionStack.Any(f => f is JudgmentFrame j && j.OriginalUseLightning?.Id == a.Id ||
                f is NullificationWindowFrame w && w.DelayedReturn?.OriginalUseLightning?.Id == a.Id ||
                f is CardEffectBeforeApplyFrame e && e.DelayedReturn?.OriginalUseLightning?.Id == a.Id)) continue;
            RemoveOriginalUseLightning(a, "holder-unavailable");
        }
    }

    private void PreserveNativeDelayedAttackCompletion(AttackCompletionReceipt completion)
    {
        if (!completion.DelayedTurn) return;
        if (_resolutionStack.LastOrDefault() is not JudgmentFrame judgment || judgment.Id != completion.ResolutionId ||
            completion.ProgramFrameId is not null || completion.DelayedTurnSeat != judgment.TargetSeat)
            throw new InvalidOperationException("Delayed attack cleanup lost its native Judgment owner.");
        if (judgment.NativeDelayedAttackCompletion is { } prior && prior != completion)
            throw new InvalidOperationException("A delayed child cannot replace its native completion receipt.");
        ReplaceRuntimeFrame(judgment.Id, judgment with { NativeDelayedAttackCompletion = completion });
    }

    private ProgramSkillFrame BindNativeDelayedAttackChild(ProgramSkillFrame child, long judgmentId)
    {
        if (_resolutionStack.LastOrDefault() is not JudgmentFrame { NativeDelayedAttackCompletion: { } completion } judgment ||
            judgment.Id != judgmentId || completion.ResolutionId != judgmentId || !completion.DelayedTurn ||
            completion.ProgramFrameId is not null || completion.DelayedTurnSeat != judgment.TargetSeat ||
            child.Id <= judgmentId || child.NativeDelayedAttackReturn is not null ||
            _resolutionStack.Any(f => f.Id == child.Id))
            throw new InvalidOperationException("A delayed cleanup child requires its exact completed native Judgment parent.");
        return child with { NativeDelayedAttackReturn = new(judgmentId, child.Id) };
    }

    private bool HasNativeDelayedAttackChild(long judgmentId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == judgmentId);
        return index >= 0 && index + 1 < _resolutionStack.Count &&
            _resolutionStack[index] is JudgmentFrame { NativeDelayedAttackCompletion: { } completion } judgment &&
            completion.ResolutionId == judgmentId && completion.DelayedTurn && completion.ProgramFrameId is null &&
            completion.DelayedTurnSeat == judgment.TargetSeat && judgment.CardAttack is not null &&
            _resolutionStack[index + 1] is ProgramSkillFrame { NativeDelayedAttackReturn: { } parentReturn } child &&
            parentReturn.JudgmentId == judgmentId && parentReturn.ChildFrameId == child.Id;
    }

    private void ResumeNativeDelayedAttackCompletion(long judgmentId)
    {
        if (_resolutionStack.LastOrDefault() is not JudgmentFrame { NativeDelayedAttackCompletion: { } completion } judgment ||
            judgment.Id != judgmentId || completion.ResolutionId != judgmentId || !completion.DelayedTurn ||
            completion.ProgramFrameId is not null || completion.DelayedTurnSeat != judgment.TargetSeat ||
            ActiveDying is not null)
            throw new InvalidOperationException("Delayed child completion lost its exact native cleanup return.");
        CompleteAttackAfterCardResolution(completion);
    }
}
