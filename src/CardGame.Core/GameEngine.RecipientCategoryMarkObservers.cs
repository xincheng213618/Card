namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactRecipientCategoryParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat) return false;
        var e = GetProgramTrigger(f).Effects.Single(); var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != c.ParentFrameId) return false;
        if (e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark)
            return c.Window == SkillProgramTriggerWindow.DrawPhaseEnded && _phase == TurnPhase.Draw &&
                _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame w && w.Window == c.Window &&
                w.Continuation == ProgramLifecycleContinuation.CompleteDrawPhaseEnded && w.OwnerSeat != f.OwnerSeat && w.OwnerSeat == c.SourceSeat &&
                c.TargetSeat == w.OwnerSeat && w.OwnerSeat == _currentSeat && w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
                MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]);
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery)
            return ExactDyingOwnedCardEntry(f, out var dying, out _) && dying.VictimSeat == f.OwnerSeat;
        if (e.Op == SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard)
            return ExactTurnDefaultStatPhaseParent(f) && c.Window == SkillProgramTriggerWindow.PlayPhaseStarting;
        return e.Op == SkillProgramEffectOp.SpendCategoryMarkForSlashTargets &&
            TryGetRecipientCategorySlashUse(f.OwnerSeat, c, out _, out var window) &&
            DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), c, window);
    }
    private bool IsRecipientCategoryGiftDrawContext(ProgramTriggerCandidate candidate, ProgramSkillWindowContext c) =>
        GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.GiveHandAndGrantCategoryMark }] &&
        c.Window == SkillProgramTriggerWindow.DrawPhaseEnded && c.SourceSeat == _currentSeat && c.TargetSeat == _currentSeat &&
        candidate.OwnerSeat != _currentSeat && _players[_currentSeat].IsAlive && _phase == TurnPhase.Draw &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } parent &&
        parent.Window == c.Window && parent.OwnerSeat == _currentSeat && parent.Continuation == ProgramLifecycleContinuation.CompleteDrawPhaseEnded;

    private bool ValidRecipientCategoryMarkReceipt(ProgramSkillFrame f)
    {
        if (f.RecipientCategoryMark is not { } r || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactRecipientCategoryParent(f) || r.Source != RecipientMarkSource(f) || r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) ||
            r.ParentWindowId != f.WindowContext!.ParentFrameId || !IsValidPlayerSeat(r.HolderSeat) ||
            GetProgramTrigger(f).Effects is not [var e] || r.Operation != e.Op || r.StateId != e.StateId ||
            r.SourceSkillId != (e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark ? f.SkillId : e.SourceBind) ||
            r.Materials.Select(m => m.CardId).Distinct().Count() != r.Materials.Count || r.SelectedCardIds.Distinct().Count() != r.SelectedCardIds.Count ||
            r.SelectedCardIds.Any(id => !r.Materials.Any(m => m.CardId == id)) || r.CandidateSeats.Distinct().Count() != r.CandidateSeats.Count ||
            r.CandidateSeats.Any(s => !IsValidPlayerSeat(s)) || r.AddedTargetSeats.Distinct().Count() != r.AddedTargetSeats.Count) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramBindingStartedEvent>().Count(x => x.FrameId == f.Id && x.SkillId == f.SkillId && x.BindingId == f.TriggerId &&
                x.SkillInstanceId == f.SkillInstanceId && x.OwnerSeat == f.OwnerSeat && x.Window == f.WindowContext.Window) != 1 ||
            history.OfType<RecipientCategoryMarkStartedEvent>().Where(x => x.FrameId == f.Id).ToArray() is not [var start] ||
            start != new RecipientCategoryMarkStartedEvent(f.Id, r.Source, r.GameplayHash, r.SourceSkillId, r.StateId, r.Operation, r.HolderSeat, r.ParentWindowId,
                e.Op == SkillProgramEffectOp.GiveHandAndGrantCategoryMark ? null : r.Token?.TokenId)) return false;
        var payments = history.OfType<RecipientCategoryMarkMovementPaidEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var consumed = history.OfType<RecipientCategoryMarkConsumedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var grants = history.OfType<RecipientCategoryMarkGrantedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var recovery = history.OfType<RecipientCategoryMarkRecoveryIssuedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var draws = history.OfType<RecipientCategoryMarkDrawIssuedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var targets = history.OfType<RecipientCategorySlashTargetsResolvedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        var completed = history.OfType<RecipientCategoryMarkCompletedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        if (r.Stage == RecipientCategoryMarkStage.Complete ? completed is not [var done] || done != new RecipientCategoryMarkCompletedEvent(f.Id, r.Operation, r.Consumed) : completed.Length != 0) return false;
        if (r.Operation == SkillProgramEffectOp.GiveHandAndGrantCategoryMark)
        {
            if (r.Consumed || consumed.Length != 0 || r.HolderSeat == f.OwnerSeat || r.HolderSeat != f.WindowContext.TargetSeat ||
                r.Materials.Any(m => m.From != CardLocation.Hand(r.HolderSeat) || m.Hidden) || r.DyingFrameId is not null || r.OriginalDyingCursor is not null || r.CardUseFrameId is not null ||
                r.ActionId is not null || r.TargetSeat is not null || r.CandidateSeats.Count != 0 || r.BaseTargetSeats.Count != 0 || r.AddedTargetSeats.Count != 0 ||
                r.RecoveryIssued || recovery.Length != 0 || r.DrawIssued || draws.Length != 0 || targets.Length != 0) return false;
            if (r.Stage == RecipientCategoryMarkStage.ChoosingGift)
                return r.Token is null && r.SelectedCardIds.Count == 0 && r.BatchId is null && r.SequenceBefore == 0 && r.SequenceAfter == 0 &&
                    payments.Length == 0 && grants.Length == 0 && f.PendingMovementContinuation is null &&
                    r.Materials.Select(m => m.CardId).SequenceEqual(GetHand(_players[r.HolderSeat]).Select(c => c.Id));
            if (r.Stage is not (RecipientCategoryMarkStage.GiftChildren or RecipientCategoryMarkStage.Complete) || r.Token is not { } token ||
                token.TokenId != f.Id || token.HolderSeat != r.HolderSeat || token.SourceSkillId != f.SkillId || token.StateId != r.StateId ||
                token.Origin != r.Source || token.OriginGameplayHash != r.GameplayHash || token.DerivedSkillId != e.SkillIds[(int)token.Kind] ||
                token.GiftSequence != r.SequenceAfter || r.SelectedCardIds is not [var cardId] || r.BatchId is not { } batch ||
                grants is not [var grant] || grant.Token != token || payments is not [var invoice] ||
                invoice != new RecipientCategoryMarkMovementPaidEvent(f.Id, r.HolderSeat, 1, r.SequenceBefore, r.SequenceAfter, batch)) return false;
            var movements = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
            var reveals = history.OfType<ProgramCardsRevealedEvent>().Where(x => x.FrameId == f.Id && x.Bind == "category-mark-gift").ToArray();
            return movements is [var move] && move.CardId == cardId && move.From == CardLocation.Hand(r.HolderSeat) && move.To == CardLocation.Hand(f.OwnerSeat) &&
                move.Reason.Value == RecipientMarkReason(f, "gift") && (GetProgramCardCategory(move.CardKind) switch {
                    SkillProgramCardCategory.Trick => RecipientCategoryMarkKind.Rescue, SkillProgramCardCategory.Equipment => RecipientCategoryMarkKind.Discard,
                    _ => RecipientCategoryMarkKind.ExtraTargets }) == token.Kind && reveals is [var reveal] && reveal.Cards is [{ Id: var shown }] && shown == cardId &&
                (r.Stage == RecipientCategoryMarkStage.Complete ? f.PendingMovementContinuation is null : f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == r.HolderSeat);
        }
        if (r.HolderSeat != f.OwnerSeat || !r.Consumed || r.Token is not { } spent || spent.HolderSeat != f.OwnerSeat || spent.SourceSkillId != r.SourceSkillId ||
            spent.StateId != r.StateId || spent.DerivedSkillId != f.SkillId || spent.Kind != RecipientMarkKind(r.Operation) || grants.Length != 0 ||
            consumed is not [var cost] || cost != new RecipientCategoryMarkConsumedEvent(f.Id, spent.TokenId, r.HolderSeat, r.SourceSkillId, r.StateId, spent.Kind) ||
            history.OfType<RecipientCategoryMarkGrantedEvent>().Count(g => g.Token == spent) != 1 ||
            history.OfType<RecipientCategoryMarkConsumedEvent>().Count(x => x.TokenId == spent.TokenId) != 1) return false;
        if (r.Operation == SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery)
        {
            if (!ExactDyingOwnedCardEntry(f, out var dying, out var entry) || r.DyingFrameId != dying.Id || !ExactRecipientCategoryDyingCursor(f, dying, entry) || r.CardUseFrameId is not null || r.ActionId is not null ||
                r.Materials.Count != 0 || r.SelectedCardIds.Count != 0 || r.TargetSeat is not null || r.CandidateSeats.Count != 0 || r.BaseTargetSeats.Count != 0 || r.AddedTargetSeats.Count != 0 ||
                r.SequenceBefore != 0 || r.SequenceAfter != 0 || r.BatchId is not null || payments.Length != 0 || targets.Length != 0 || !r.RecoveryIssued || r.RecoveryAmount <= 0 ||
                recovery is not [var heal] || heal != new RecipientCategoryMarkRecoveryIssuedEvent(f.Id, r.HolderSeat, r.RecoveryAmount)) return false;
            if (r.Stage == RecipientCategoryMarkStage.RecoveryChildren) return !r.DrawIssued && draws.Length == 0 &&
                f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } recovering && recovering.SubjectSeat == r.HolderSeat;
            if (r.Stage is not (RecipientCategoryMarkStage.DrawChildren or RecipientCategoryMarkStage.Complete) || !r.DrawIssued || r.DrawActual is < 0 or > 1 ||
                r.DrawBefore < 0 || r.DrawAfter < r.DrawBefore || r.DrawAfter > _movementSequence || draws is not [var draw] ||
                draw != new RecipientCategoryMarkDrawIssuedEvent(f.Id, r.HolderSeat, r.DrawActual, r.DrawBefore, r.DrawAfter)) return false;
            var drawn = _cardMovements.Where(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter && m.Reason.Value == RecipientMarkReason(f, "draw")).ToArray();
            return drawn.Length == r.DrawActual && drawn.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.HolderSeat)) &&
                (r.Stage == RecipientCategoryMarkStage.Complete ? f.PendingMovementContinuation is null : f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == r.HolderSeat);
        }
        if (r.DyingFrameId is not null || r.OriginalDyingCursor is not null || r.RecoveryIssued || recovery.Length != 0 || r.DrawIssued || draws.Length != 0) return false;
        if (r.Operation == SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard)
        {
            if (r.CardUseFrameId is not null || r.ActionId is not null || r.BaseTargetSeats.Count != 0 || r.AddedTargetSeats.Count != 0 || targets.Length != 0 || r.SelectedCardIds.Count > 2) return false;
            if (r.Stage == RecipientCategoryMarkStage.ChoosingTarget)
                return r.TargetSeat is null && r.Materials.Count == 0 && r.SelectedCardIds.Count == 0 && payments.Length == 0 && f.PendingMovementContinuation is null;
            if (r.TargetSeat is not { } target || !r.CandidateSeats.Contains(target) || r.Materials.Any(m => m.From.OwnerSeat != target ||
                m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) || m.Hidden != (m.From.Zone == CardZoneKind.Hand && target != f.OwnerSeat))) return false;
            if (r.Stage == RecipientCategoryMarkStage.ChoosingCards || r.Stage == RecipientCategoryMarkStage.Complete && r.SelectedCardIds.Count == 0)
                return payments.Length == 0 && r.BatchId is null && r.SequenceBefore == 0 && r.SequenceAfter == 0 && f.PendingMovementContinuation is null;
            if (r.Stage is not (RecipientCategoryMarkStage.DiscardChildren or RecipientCategoryMarkStage.Complete) || r.SelectedCardIds.Count == 0 ||
                r.BatchId is not { } batch || r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.SequenceAfter > _movementSequence || payments is not [var invoice] ||
                invoice != new RecipientCategoryMarkMovementPaidEvent(f.Id, target, r.SelectedCardIds.Count, r.SequenceBefore, r.SequenceAfter, batch)) return false;
            var moves = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
            return moves.Length == r.SelectedCardIds.Count && moves.Select(m => m.CardId).SequenceEqual(r.SelectedCardIds) && moves.All(m =>
                m.From == r.Materials.Single(x => x.CardId == m.CardId).From && m.Reason.Value == RecipientMarkReason(f, "discard") &&
                m.To == (m.From.Zone == CardZoneKind.Equipment && GetAdvancedCard(m.CardId).IsGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile)) &&
                (r.Stage == RecipientCategoryMarkStage.Complete ? f.PendingMovementContinuation is null : f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == target);
        }
        if (r.Operation != SkillProgramEffectOp.SpendCategoryMarkForSlashTargets || r.CardUseFrameId is not { } useId || r.ActionId is not { } actionId ||
            r.Materials.Count != 0 || r.SelectedCardIds.Count != 0 || r.TargetSeat is not null || r.SequenceBefore != 0 || r.SequenceAfter != 0 || r.BatchId is not null ||
            payments.Length != 0 || f.PendingMovementContinuation is not null || r.AddedTargetSeats.Count > 2 || r.AddedTargetSeats.Any(s => !r.CandidateSeats.Contains(s) || r.BaseTargetSeats.Contains(s)) ||
            LifecycleCardUse(useId) is not { Action: { } action } slash || action.ActionId != actionId) return false;
        return r.Stage == RecipientCategoryMarkStage.ChoosingSlashTargets ? targets.Length == 0 && r.BaseTargetSeats.SequenceEqual(slash.TargetSeats) :
            r.Stage == RecipientCategoryMarkStage.Complete && targets is [var resolved] && IsRecipientCategorySlashTargetFact(slash, resolved, r.BaseTargetSeats);
    }
    private bool RecipientCategoryMarkFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.RecipientCategoryMark is not { } r || !ValidRecipientCategoryMarkReceipt(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved && r.Stage is RecipientCategoryMarkStage.GiftChildren or RecipientCategoryMarkStage.DiscardChildren or RecipientCategoryMarkStage.DrawChildren)
        {
            var before = r.Stage == RecipientCategoryMarkStage.DrawChildren ? r.DrawBefore : r.SequenceBefore;
            var after = r.Stage == RecipientCategoryMarkStage.DrawChildren ? r.DrawAfter : r.SequenceAfter;
            var reason = RecipientMarkReason(f, r.Stage == RecipientCategoryMarkStage.DrawChildren ? "draw" : r.Stage == RecipientCategoryMarkStage.GiftChildren ? "gift" : "discard");
            return moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == f.Id && moved.Batch.AwaitingProgramFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > before && m.Sequence <= after && m.Reason.Value == reason &&
                        (r.Stage == RecipientCategoryMarkStage.DrawChildren ? m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.HolderSeat) :
                         moved.Batch.Id == r.BatchId && r.SelectedCardIds.Contains(m.CardId) && m.From == r.Materials.Single(x => x.CardId == m.CardId).From) ||
                     r.Stage == RecipientCategoryMarkStage.DrawChildren && m.Sequence > before && m.Sequence <= after && m.Reason == CardMoveReasons.Reshuffle && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile ||
                     r.Stage == RecipientCategoryMarkStage.DiscardChildren && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(r.TargetSeat!.Value) && m.To == CardLocation.DiscardPile && RecipientCategoryPaidLionOrOx(f, CardKind.WoodenOx)));
        }
        var recoveryStage = r.Stage == RecipientCategoryMarkStage.RecoveryChildren;
        var lion = r.Stage == RecipientCategoryMarkStage.DiscardChildren && RecipientCategoryPaidLionOrOx(f, CardKind.SilverLion);
        var target = recoveryStage ? r.HolderSeat : r.TargetSeat;
        if ((recoveryStage || lion) && child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == target && hp.Change.TargetSeat == target && hp.Change.Amount == (recoveryStage ? r.RecoveryAmount : 1);
        if ((recoveryStage || lion) && child is RecoveryReplacementFrame recovery)
            return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                recovery.Attempt.SourceSeat == target && recovery.Attempt.TargetSeat == target && recovery.Attempt.Amount == (recoveryStage ? r.RecoveryAmount : 1) &&
                (recoveryStage ? recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex == 1 :
                    recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == RecipientMarkReason(f, "discard"));
        return r.Stage == RecipientCategoryMarkStage.GiftChildren && r.Token is { } token && child is ProgramLifecycleTriggerWindowFrame skills &&
            skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
            skills.OwnerSeat == token.HolderSeat && CompleteProgramEventHistory().OfType<SkillsAcquiredEvent>().Any(e => e.PlayerSeat == token.HolderSeat &&
                e.SourceSkillId == $"recipient-category:{token.HolderSeat}:{token.SourceSkillId}:{token.StateId}" && e.SkillIds.Contains(token.DerivedSkillId));
    }
    private bool RecipientCategoryPaidLionOrOx(ProgramSkillFrame f, CardKind kind) => f.RecipientCategoryMark is { Stage: RecipientCategoryMarkStage.DiscardChildren } r &&
        _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && r.SelectedCardIds.Contains(m.CardId) && m.CardKind == kind &&
            m.From == CardLocation.Equipment(r.TargetSeat!.Value) && m.To == CardLocation.DiscardPile && m.Reason.Value == RecipientMarkReason(f, "discard"));
    private bool RecipientCategoryMarkStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame f && RecipientCategoryMarkFirstChild(f, child) ||
        child is ProgramSkillFrame { RecipientCategoryMark: not null } observer && observer.WindowContext?.ParentFrameId == parent.Id && ValidRecipientCategoryMarkReceipt(observer);
    private bool IsRecipientCategoryMarkChoice(ProgramSkillFrame f, PendingDecision p)
    {
        if (f.RecipientCategoryMark is not { } r || !ValidRecipientCategoryMarkReceipt(f) || p.Kind != DecisionKind.ProgramTrigger || !p.IsPrivate ||
            p.PlayerSeat != (r.Stage == RecipientCategoryMarkStage.ChoosingGift ? r.HolderSeat : f.OwnerSeat) || p.SourceSeat != f.OwnerSeat || p.TargetSeat != p.PlayerSeat ||
            p.ValidContentIds.Count != 0 || p.RequiredCardCount != 0 || p.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = RecipientCategoryMarkChoices(f);
        return expected.Count > 0 && p.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards).Distinct()) && p.ValidTargetSeats.SequenceEqual(expected.SelectMany(c => c.Targets).Distinct()) &&
            p.Choices.Count == expected.Count && p.Choices.Zip(expected).All(pair => SameNameHandChoicesEqual(pair.First, pair.Second));
    }
    private void AssertRecipientCategoryMark(ProgramSkillFrame f)
    {
        if (f.RecipientCategoryMark is null)
        {
            if (f.TriggerId is null || GetProgramTrigger(f).Effects.All(e => !RecipientCategoryMarksComposition.IsOperation(e.Op))) return;
            if (CompleteProgramEventHistory().OfType<RecipientCategoryMarkStartedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("An issued category mark operation lost its owning receipt.");
            return;
        }
        if (!ValidRecipientCategoryMarkReceipt(f)) throw new InvalidOperationException("A category mark operation lost its exact native parent, token, choices or payment invoice.");
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i + 1 < _resolutionStack.Count && !RecipientCategoryMarkFirstChild(f, _resolutionStack[i + 1])) throw new InvalidOperationException("A category mark retained an unrelated native child.");
        if (i == _resolutionStack.Count - 1 && _pendingDecision is { } prompt && f.RecipientCategoryMark.Stage is
            RecipientCategoryMarkStage.ChoosingGift or RecipientCategoryMarkStage.ChoosingTarget or RecipientCategoryMarkStage.ChoosingCards or RecipientCategoryMarkStage.ChoosingSlashTargets &&
            !IsRecipientCategoryMarkChoice(f, prompt)) throw new InvalidOperationException("A category mark changed its exact private published choice.");
    }
    private void AssertRecipientCategoryMarkState()
    {
        if (!_recipientCategoryMarkLedgerStarted && _recipientCategoryMarks.Count == 0) return;
        var history = CompleteProgramEventHistory().Where(e => e is RecipientCategoryMarkGrantedEvent or RecipientCategoryMarkConsumedEvent or RecipientCategoryMarkStartedEvent or
            RecipientCategoryMarkMovementPaidEvent or ProgramBindingStartedEvent or ProgramCardsRevealedEvent).ToArray();
        var facts = history.Where(e => e is RecipientCategoryMarkGrantedEvent or RecipientCategoryMarkConsumedEvent).ToArray();
        var expected = new Dictionary<RecipientCategoryMarkKey, RecipientCategoryMarkToken>(); var ids = new HashSet<long>();
        foreach (var fact in facts)
        {
            if (fact is RecipientCategoryMarkGrantedEvent g)
            {
                var t = g.Token; var key = new RecipientCategoryMarkKey(t.HolderSeat, t.SourceSkillId, t.StateId);
                if (g.FrameId != t.TokenId || !ids.Add(t.TokenId) || !Enum.IsDefined(t.Kind) || !IsValidPlayerSeat(t.HolderSeat) || !IsValidPlayerSeat(t.Origin.OwnerSeat) ||
                    t.Origin.SkillId != t.SourceSkillId || t.Origin.OwnerSeat == t.HolderSeat || !expected.TryAdd(key, t) ||
                    _contentRegistry.GetSkill(t.SourceSkillId).Program is not { } program || program.GameplayHash != t.OriginGameplayHash ||
                    program.Triggers.SingleOrDefault(b => b.Id == t.Origin.BindingId) is not { Window: SkillProgramTriggerWindow.DrawPhaseEnded } binding ||
                    binding.Effects is not [{ Op: SkillProgramEffectOp.GiveHandAndGrantCategoryMark } effect] || effect.StateId != t.StateId || effect.SkillIds[(int)t.Kind] != t.DerivedSkillId ||
                    history.OfType<RecipientCategoryMarkStartedEvent>().Count(e => e.FrameId == g.FrameId && e.Source == t.Origin && e.GameplayHash == t.OriginGameplayHash &&
                        e.SourceSkillId == t.SourceSkillId && e.StateId == t.StateId && e.Operation == effect.Op && e.HolderSeat == t.HolderSeat && e.TokenId is null) != 1 ||
                    history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == g.FrameId && e.OwnerSeat == t.Origin.OwnerSeat && e.SkillId == t.SourceSkillId &&
                        e.BindingId == t.Origin.BindingId && e.SkillInstanceId == t.Origin.SkillInstanceId && e.Window == binding.Window) != 1 ||
                    _cardMovements.SingleOrDefault(m => m.Sequence == t.GiftSequence) is not { } moved || moved.From != CardLocation.Hand(t.HolderSeat) || moved.To != CardLocation.Hand(t.Origin.OwnerSeat) ||
                    moved.Reason.Value != $"skill-program.{t.SourceSkillId}.recipient-category.gift" ||
                    (GetProgramCardCategory(moved.CardKind) switch { SkillProgramCardCategory.Trick => RecipientCategoryMarkKind.Rescue,
                        SkillProgramCardCategory.Equipment => RecipientCategoryMarkKind.Discard, _ => RecipientCategoryMarkKind.ExtraTargets }) != t.Kind ||
                    history.OfType<ProgramCardsRevealedEvent>().Count(e => e.FrameId == g.FrameId && e.SkillId == t.SourceSkillId && e.BindingId == t.Origin.BindingId &&
                        e.OwnerSeat == t.HolderSeat && e.Bind == "category-mark-gift" && e.Cards is [{ Id: var revealed }] && revealed == moved.CardId) != 1)
                    throw new InvalidOperationException("A recipient mark lost its original face-up gift identity.");
            }
            else if (fact is RecipientCategoryMarkConsumedEvent c)
            {
                var key = new RecipientCategoryMarkKey(c.HolderSeat, c.SourceSkillId, c.StateId);
                if (!expected.Remove(key, out var token) || token.TokenId != c.TokenId || token.Kind != c.Kind) throw new InvalidOperationException("A recipient mark was consumed without its exact held token.");
            }
        }
        if (expected.Count != _recipientCategoryMarks.Count || expected.Any(p => _recipientCategoryMarks.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Recipient mark state diverged from its grants and consumptions.");
        foreach (var holder in _players)
            foreach (var kind in Enum.GetValues<RecipientCategoryMarkKind>())
            {
                var count = expected.Values.Count(t => t.HolderSeat == holder.Seat && t.Kind == kind); var marker = RecipientPublicMarker(kind);
                if (holder.Markers.GetValueOrDefault(marker) != count || holder.MarkerSourceCounts.GetValueOrDefault((marker, holder.Seat)) != count)
                    throw new InvalidOperationException("A public category badge diverged from its actual recipient-held token.");
            }
    }
    private IReadOnlyList<CardSnapshot> RecipientCategoryMarkPublicCards() => Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.RecipientCategoryMark is { Operation: SkillProgramEffectOp.GiveHandAndGrantCategoryMark,
            Stage: RecipientCategoryMarkStage.GiftChildren or RecipientCategoryMarkStage.Complete, Token: not null } && ValidRecipientCategoryMarkReceipt(f))
        .SelectMany(f => CompleteProgramEventHistory().OfType<ProgramCardsRevealedEvent>().Where(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
            e.BindingId == f.TriggerId && e.OwnerSeat == f.RecipientCategoryMark!.HolderSeat && e.Bind == "category-mark-gift").SelectMany(e => e.Cards))
        .GroupBy(c => c.Id).Select(g => g.Single()).ToArray());
}
