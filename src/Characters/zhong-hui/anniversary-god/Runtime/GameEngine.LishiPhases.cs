namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool LishiOptionMatchesPhase(int option, ActualDiscardRecoveryPhaseKind kind) => option switch
    { 1 => kind is ActualDiscardRecoveryPhaseKind.Preparation or ActualDiscardRecoveryPhaseKind.Ending, 2 => kind == ActualDiscardRecoveryPhaseKind.Judgment, 3 => kind == ActualDiscardRecoveryPhaseKind.Draw, 4 => kind == ActualDiscardRecoveryPhaseKind.Play, 5 => kind == ActualDiscardRecoveryPhaseKind.Discard, _ => false };
    private IEnumerable<LishiPunishmentIssuedEvent> LishiActiveEffects(ActualDiscardRecoveryPhaseKey phase)
    {
        if (!HasLishiCapability || ProgramEventHistory<ActualPhaseEndedEvent>().Any(e => e.Phase == phase)) return [];
        var applied = ProgramEventHistory<LishiPhaseAppliedEvent>().Where(e => e.Phase == phase).ToArray();
        return ProgramEventHistory<LishiPunishmentIssuedEvent>().Where(e => e.TargetSeat == phase.ActorSeat && applied.Any(a => a.IssueFrameId == e.FrameId && a.TargetSeat == e.TargetSeat && a.Option == e.Option));
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 39300)] private void ObserveLishiPhase(IGameEvent payload)
    {
        if (!HasLishiCapability) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { Type: CardActionType.Use } action } use &&
            use.SourceSeat == declared.SourceSeat && use.CardId == declared.CardId && use.CardKind == declared.CardKind && action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind)
            RecordLishiHandUse(action);
        if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } accepted)
            RecordLishiHandUse(accepted.Action);
        if (payload is ActualDiscardRecoveryPhaseStartedEvent started)
        {
            var phase = started.Phase;
            foreach (var issue in ProgramEventHistory<LishiPunishmentIssuedEvent>().Where(e => e.TargetSeat == phase.ActorSeat && e.PhaseToken < phase.Token && LishiOptionMatchesPhase(e.Option, phase.Kind)).ToArray())
            {
                if (ProgramEventHistory<LishiPhaseAppliedEvent>().Any(a => a.IssueFrameId == issue.FrameId && a.TargetSeat == issue.TargetSeat && a.Option == issue.Option && a.Phase.Kind == phase.Kind)) continue;
                AdvanceEventRulesAndQueueFact(new LishiPhaseAppliedEvent(issue.FrameId, issue.TargetSeat, issue.Option, phase));
                if (issue.Option == 1) _currentTurnSkillSuppressionRevision++;
            }
        }
        if (payload is ActualPhaseEndedEvent ended && ProgramEventHistory<LishiPhaseAppliedEvent>().Any(a => a.Option == 1 && a.Phase == ended.Phase)) _currentTurnSkillSuppressionRevision++;
    }
    private bool IsLishiPhaseSuppressed(int seat) => HasLishiCapability && CurrentActualDiscardRecoveryPhase() is { } phase && phase.ActorSeat == seat &&
        phase.Kind is ActualDiscardRecoveryPhaseKind.Preparation or ActualDiscardRecoveryPhaseKind.Ending && LishiActiveEffects(phase).Any(e => e.Option == 1);
    private static string LishiCategory(CardKind kind) => CardUseCategoryCatalog.Get(kind) switch
    { CardUseCategories.Basic => "basic", CardUseCategories.Equipment => "equipment", _ => "trick" };
    private bool HasHappyLishiActualUseParticipant(int actor) => HasLishiCapability &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == actor &&
        LishiActiveEffects(phase).Any(e => e.Option == 4 && e.Source.SkillId == ShenZhongHuiComposition.HappyLishi);
    private bool LishiHandTypeSpent(int actor, CardKind kind, IReadOnlyList<int> ids, long? existing = null)
    {
        if (!HasLishiCapability || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != actor) return false;
        var hasHand = ids.Any(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Hand(actor));
        if (!LishiActiveEffects(phase).Any(e => e.Option == 4 && (hasHand || e.Source.SkillId == ShenZhongHuiComposition.HappyLishi))) return false;
        return ProgramEventHistory<LishiHandTypeUsedEvent>().Any(e => e.Phase == phase && e.ActorSeat == actor && e.Category == LishiCategory(kind) && e.ActionId != existing);
    }
    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 42100)]
    private bool HappyLishiAnyUseForbidden(ProgramCardUseQuery q) => q.ActionType == CardActionType.Use && HasHappyLishiActualUseParticipant(q.ActorSeat) &&
        LishiHandTypeSpent(q.ActorSeat, q.Kind, [], q.ExistingUseFrameId is { } id ? LifecycleCardUse(id)?.Action?.ActionId : null);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation, 39300)] private void LishiSubmittedHandForbidden(ProgramSubmittedUseQuery q)
    { if (LishiHandTypeSpent(q.ActorSeat, q.Kind, q.CardIds)) throw new InvalidOperationException("Lishi permits only one actual hand-card use of each type in the issued Play phase."); }
    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 39300)] private bool LishiProjectedHandForbidden(ProgramLegalActionQuery q) =>
        q.Action.CardId is { } id && q.EffectiveKind is { } kind && LishiHandTypeSpent(q.Actor.Seat, kind, [id]);
    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 39300)] private bool LishiMaterialForbidden(ProgramCardMaterialQuery q) => !q.IsResponse && LishiHandTypeSpent(q.ActorSeat, q.Kind, q.CardIds.ToArray());
    private bool IsLishiResponseUse(int actor, CardKind kind) => kind == CardKind.Nullification || kind == CardKind.Dodge && ActiveCardAttack is { } attack && attack.TargetSeat == actor && attack.EffectiveCardKind is { } incoming && IsSlashCard(incoming);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan, 39300)] private bool LishiResponseHandForbidden(ProgramNativeResponseQuery q) =>
        IsLishiResponseUse(q.Provider.Seat, q.Kind) && LishiHandTypeSpent(q.Provider.Seat, q.Kind, q.CardIds);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 39301)] private void RecordLishiHandUse(CardActionContext action)
    {
        if (!HasLishiCapability || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != action.ActorSeat ||
            action.Type != CardActionType.Use && !(IsLishiResponseUse(action.ActorSeat, action.EffectiveKind) && IsCompletedResponseUse(action, action.EffectiveKind == CardKind.Nullification ? ProgramCardContinuation.NullificationResponse : ProgramCardContinuation.Dodge)) || !LishiActiveEffects(phase).Any(e => e.Option == 4 && (e.Source.SkillId == ShenZhongHuiComposition.HappyLishi || action.PhysicalCards.Any(c => c.From == CardLocation.Hand(action.ActorSeat)))) ||
            ProgramEventHistory<LishiHandTypeUsedEvent>().Any(e => e.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new LishiHandTypeUsedEvent(action.ActionId, action.ActorSeat, LishiCategory(action.EffectiveKind), phase));
    }
    private CardLocation? LishiDiscardOrigin(int cardId, CardLocation from, CardMoveReason reason, long before)
    {
        if (IsOwnedDiscardSource(from)) return IsProgramDiscardOriginReason(reason) ? from : null;
        if (from != CardLocation.Processing) return null;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == cardId && m.Sequence <= before);
        return entry is not null && entry.To == CardLocation.Processing && IsOwnedDiscardSource(entry.From) && IsProgramDiscardOriginReason(entry.Reason) &&
            (reason == entry.Reason || reason.Value == entry.Reason.Value + "-finished" || reason.Value == entry.Reason.Value + "-discard") ? entry.From : null;
    }
    private LishiPunishmentIssuedEvent? LishiDiscardClaim(int actor) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase && phase.ActorSeat == actor ?
        LishiActiveEffects(phase).Where(e => e.Option == 5 && _players[e.Source.OwnerSeat].IsAlive)
            .OrderBy(e => RelativeTableOrder(_currentSeat, e.Source.OwnerSeat)).ThenBy(e => e.FrameId).FirstOrDefault() : null;
    private CardLocation NormalizeLishiDiscardDestination(Card card, CardLocation from, CardLocation to, CardMoveReason reason)
    {
        if (to != CardLocation.DiscardPile || !HasLishiCapability || LishiDiscardOrigin(card.Id, from, reason, _movementSequence) is not { OwnerSeat: { } owner } || LishiDiscardClaim(owner) is not { } claim) return to;
        return CardLocation.Hand(claim.Source.OwnerSeat);
    }
    // RecordMovement calls this before payment callbacks and movement observers.
    private void RecordLishiDiscardRedirection(CardMovementRecord move)
    {
        if (!HasLishiCapability || move.To.Zone != CardZoneKind.Hand || LishiDiscardOrigin(move.CardId, move.From, move.Reason, move.Sequence - 1) is not { OwnerSeat: { } target } original ||
            LishiDiscardClaim(target) is not { } claim || move.To != CardLocation.Hand(claim.Source.OwnerSeat) || CurrentActualDiscardRecoveryPhase() is not { } phase ||
            ProgramEventHistory<LishiDiscardRedirectedEvent>().Any(e => e.MovementSequence == move.Sequence)) return;
        AdvanceEventRulesAndQueueFact(new LishiDiscardRedirectedEvent(move.Sequence, claim.FrameId, claim.Source.OwnerSeat, target, move.CardId, original, move.From, move.Reason, phase));
    }
    private bool IsLishiPaidDiscardMovement(CardMovementRecord move, CardLocation expectedSource, CardMoveReason expectedReason) => HasLishiCapability && move.Reason == expectedReason &&
        ProgramEventHistory<LishiDiscardRedirectedEvent>().Any(e => e.MovementSequence == move.Sequence && e.CardId == move.CardId && e.OriginalFrom == expectedSource && e.ActualFrom == move.From && e.Reason == expectedReason &&
            move.To == CardLocation.Hand(e.SourceSeat) && e.TargetSeat == e.Phase.ActorSeat && e.Phase.Kind == ActualDiscardRecoveryPhaseKind.Discard && IsRecordedActualDiscardRecoveryPhase(e.Phase) &&
            ProgramEventHistory<LishiPunishmentIssuedEvent>().Any(i => i.FrameId == e.IssueFrameId && i.Source.OwnerSeat == e.SourceSeat && i.TargetSeat == e.TargetSeat && i.Option == 5));
    private void SettleLishiDrawPhase(ProgramSkillFrame f)
    {
        var r = f.ShenZhongHui!; var phase = r.Phase!;
        var invoices = ProgramEventHistory<NativeDrawInvocationRecordedEvent>().Where(e => e.OwnerSeat == f.OwnerSeat && e.Phase == phase).ToArray();
        var drawn = invoices.SelectMany(d => _cardMovements.Where(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == d.Reason)).Select(m => m.CardId).Distinct().Select(GetAdvancedCard).ToArray();
        var allowed = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Discard).SelectMany(c => c.Cards).ToHashSet();
        var discard = drawn.Length > 0 && drawn.Select(c => SuitColor(EffectiveSuit(_players[f.OwnerSeat], c))).Distinct().Count() == 1 ? drawn.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Hand(f.OwnerSeat) && allowed.Contains(c.Id) && !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, c, CardLocation.Hand(f.OwnerSeat), OwnedCardMoveIntent.Discard)).ToArray() : [];
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Before = before, After = before, Stage = ShenZhongHuiStage.Complete }, PendingMovementContinuation = discard.Length > 0 ? new(f.OwnerSeat, 0, null) : null });
        if (discard.Length > 0)
        {
            MoveCards(discard, CardLocation.Hand(f.OwnerSeat), CardLocation.DiscardPile, new("skill-program.anniversary:lishi.draw-discard"));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShenZhongHui = f.ShenZhongHui! with { After = _movementSequence } });
            foreach (var c in discard) if (_cardMovements.LastOrDefault(m => m.CardId == c.Id && m.Sequence > before) is { } paid) AdvanceEventRulesAndQueueFact(new LishiDrawDiscardedEvent(f.Id, c.Id, phase, paid.Sequence));
        }
        AdvanceEventRulesAndQueueFact(new LishiDrawPhaseSettledEvent(f.Id, phase, drawn.Length, discard.Length));
    }
    private string LishiJudgmentReason(ShenZhongHuiReceipt r) => CardCatalog.Get(r.Judgments[r.JudgmentIndex]).DisplayName;
    private bool CanBeginLishiJudgment(ProgramSkillFrame f, int targetSeat, string reason, string bind, SkillProgramCardSetVisibility visibility) =>
        f.SkillId is (ShenZhongHuiComposition.Curse or ShenZhongHuiComposition.HappyCurse) && f.TriggerId == "judge-debt" && f.ShenZhongHui is { Operation: SkillProgramEffectOp.ResolveLishiPhase, Stage: ShenZhongHuiStage.PhaseJudging, JudgmentId: > 0, Phase: { Kind: ActualDiscardRecoveryPhaseKind.Judgment } phase } r &&
        r.Judgments.Count == 2 && r.JudgmentIndex < 2 && targetSeat == f.OwnerSeat && phase.ActorSeat == targetSeat && CurrentActualDiscardRecoveryPhase() == phase && reason == LishiJudgmentReason(r) && bind == "lishi-judgment" && visibility == SkillProgramCardSetVisibility.Public && LishiActiveEffects(phase).Any(e => e.Option == 2);
    private bool IsLishiJudgment(JudgmentFrame j, ProgramSkillFrame f) => j.Continuation == JudgmentContinuationKind.ProgramSkill && j.ParentFrameId == f.Id && f.ShenZhongHui!.JudgmentId == j.Id && j.SourceSeat == f.OwnerSeat &&
        CanBeginLishiJudgment(f, j.TargetSeat, j.Reason, j.ProgramResultBind ?? "", j.ProgramResultVisibility ?? SkillProgramCardSetVisibility.Private);
    private void BeginLishiJudgment(ProgramSkillFrame f)
    {
        var r = f.ShenZhongHui!;
        ReplaceRuntimeTop(f = f with { ShenZhongHui = r = r with { Stage = ShenZhongHuiStage.PhaseJudging, JudgmentId = _resolutionSequence + 1, ResultId = null, ResultSuit = Suit.None, ResultRank = 0 } });
        _ = BeginJudgment(null, f.OwnerSeat, LishiJudgmentReason(r), f.Id, null, JudgmentContinuationKind.ProgramSkill, sourceSeat: f.OwnerSeat, programResultBind: "lishi-judgment", programResultVisibility: SkillProgramCardSetVisibility.Public);
    }
    private bool TryReturnLishiJudgment(JudgmentFrame j)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenZhongHui: { Stage: ShenZhongHuiStage.PhaseJudging } r } f || f.Id != j.ParentFrameId) return false;
        if (!IsLishiJudgment(j, f) || _resolutionStack.Any(x => x.Id == j.Id)) throw new InvalidOperationException("Lishi's native judgment lost its exact original phase return.");
        var results = ProgramEventHistory<JudgmentResolvedEvent>().Where(e => e.ResolutionId == j.Id && e.ParentResolutionId == f.Id && e.TargetSeat == f.OwnerSeat && e.Reason == LishiJudgmentReason(r)).ToArray();
        if (results is not [var result]) throw new InvalidOperationException("Lishi needs one real final judgment outcome.");
        ReplaceRuntimeTop(f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.PhaseResult, ResultId = result.CardId, ResultSuit = result.Suit ?? Suit.None, ResultRank = result.Rank ?? 0 } });
        AdvanceEventRulesAndQueueFact(new LishiJudgmentReturnedEvent(f.Id, j.Id, r.JudgmentIndex, result.CardId, result.Suit ?? Suit.None, result.Rank ?? 0)); ContinueShenZh(f.Id); return true;
    }
    private void ContinueLishiJudgments(ProgramSkillFrame f)
    {
        var r = f.ShenZhongHui!;
        if (r.Stage == ShenZhongHuiStage.PhaseResult && r.ResultId is { } id && _cardZones.GetLocation(id) == CardLocation.Processing)
        {
            if (_cardMovements.LastOrDefault(m => m.CardId == id) is not { From.Zone: CardZoneKind.Judgment, To.Zone: CardZoneKind.Processing } paid || paid.Reason.Value != "skill-program.judgment.result") throw new InvalidOperationException("Lishi cannot discard a judgment entity without the native final-result transfer.");
            ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.PhaseCleanup, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DiscardPile, new("skill-program.anniversary:lishi.judgment-finish"));
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShenZhongHui = f.ShenZhongHui! with { After = _movementSequence } }); ContinueShenZh(f.Id); return;
        }
        if (r.Stage is ShenZhongHuiStage.PhaseResult or ShenZhongHuiStage.PhaseCleanup)
        {
            var kind = r.Judgments[r.JudgmentIndex];
            ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { Stage = ShenZhongHuiStage.PhaseNext }, PendingMovementContinuation = null });
            if (r.ResultId is not null)
            {
                if (kind == CardKind.Indulgence && r.ResultSuit != Suit.Heart) _pendingTurnDelayedEffects |= DelayedTurnEffects.SkipPlayPhase;
                if (kind == CardKind.SupplyShortage && r.ResultSuit != Suit.Club) _pendingTurnDelayedEffects |= DelayedTurnEffects.SkipDrawPhase;
                if (kind == CardKind.Lightning && r.ResultSuit == Suit.Spade && r.ResultRank is >= 2 and <= 9)
                { BeginProgramSkillDamage(f, f.OwnerSeat, 3, nature: DamageNature.Thunder, sourceLess: true); return; }
            }
        }
        f = GetActiveProgramFrame(f.Id); r = f.ShenZhongHui!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || r.JudgmentIndex + 1 >= r.Judgments.Count) { FinishShenZh(f); return; }
        ReplaceRuntimeTop(f = f with { ShenZhongHui = r with { JudgmentIndex = r.JudgmentIndex + 1 } }); BeginLishiJudgment(f);
    }
}
