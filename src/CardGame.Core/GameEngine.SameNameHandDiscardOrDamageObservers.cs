namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidSameNameHandReceipt(ProgramSkillFrame f)
    {
        if (f.SameNameHandDiscardOrDamage is not { } r || f.TriggerId is null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !SameNameHandActionParent(f.OwnerSeat, context, out var window) ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            r.ActionId != window.Action.ActionId || r.CardWindowId != window.Id || r.OriginalParentFrameId != window.ParentFrameId ||
            r.EffectiveKind != window.Action.EffectiveKind || r.NormalizedName != ProgramBasicCardName(r.EffectiveKind) ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat || r.ActualTurnOwnerSeat == f.OwnerSeat || !Enum.IsDefined(r.Stage) ||
            r.CandidateSeats.Count == 0 || r.CandidateSeats.Distinct().Count() != r.CandidateSeats.Count ||
            r.CandidateSeats.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            CreateCardActionProgramContext(window, window.Candidates[window.CandidateIndex]) != context) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != window.Id) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Trigger is not { } trigger || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || !trigger.Optional ||
            trigger.Window != context.Window || plan.Instructions is not [{ Op: SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage,
                Target: SkillProgramEffectTarget.Owner, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }]) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
                e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == context.Window) != 1 ||
            history.OfType<SameNameHandStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started != new SameNameHandStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActionId, r.CardWindowId, r.OriginalParentFrameId,
                r.EffectiveKind, r.NormalizedName, r.ActualTurnNumber, r.ActualTurnOwnerSeat) ||
            history.OfType<SameNameHandStartedEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId && e.ActionId == r.ActionId) != 1)
            return false;
        var targets = history.OfType<SameNameHandTargetSelectedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var payments = history.OfType<SameNameHandDiscardPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var damage = history.OfType<SameNameHandDamageIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completions = history.OfType<SameNameHandCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == SameNameHandStage.Complete ? completions is not [var completed] ||
                completed != new SameNameHandCompletedEvent(f.Id, r.TargetSeat, r.PaidMaterial is not null, r.DamageIssued) : completions.Length != 0) return false;
        if (r.TargetSeat is null)
            return r.Stage is SameNameHandStage.ChoosingTarget or SameNameHandStage.Complete && targets.Length == 0 && payments.Length == 0 && damage.Length == 0 &&
                r.EligibleMaterials.Count == 0 && r.PaidMaterial is null && !r.DamageIssued && f.PendingMovementContinuation is null &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null && (r.Stage != SameNameHandStage.Complete ||
                    !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId));
        var target = r.TargetSeat.Value;
        if (!r.CandidateSeats.Contains(target) || targets is not [var chosen] || chosen.TargetSeat != target ||
            r.EligibleMaterials.Select(m => m.CardId).Distinct().Count() != r.EligibleMaterials.Count || r.EligibleMaterials.Any(m =>
                m.CardId <= 0 || m.From != CardLocation.Hand(target) || ProgramBasicCardName(m.PrintedKind) != r.NormalizedName)) return false;
        if (r.Stage == SameNameHandStage.ChoosingPayment)
            return r.PaidMaterial is null && !r.DamageIssued && r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null &&
                payments.Length == 0 && damage.Length == 0 && f.PendingMovementContinuation is null &&
                r.EligibleMaterials.SequenceEqual(SameNameHandMaterials(target, r.NormalizedName));
        if (r.Stage == SameNameHandStage.Complete && r.PaidMaterial is null && !r.DamageIssued)
            return r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null && payments.Length == 0 && damage.Length == 0 &&
                f.PendingMovementContinuation is null && (!_players[target].IsAlive || !_players[f.OwnerSeat].IsAlive);
        if (r.Stage is SameNameHandStage.DiscardChildren or SameNameHandStage.Complete && r.PaidMaterial is { } paid)
        {
            if (r.DamageIssued || damage.Length != 0 || !r.EligibleMaterials.Contains(paid) || r.BatchId is not { } batch ||
                r.SequenceBefore < 0 || r.SequenceAfter <= r.SequenceBefore || r.SequenceAfter > _movementSequence || payments is not [var invoice] ||
                invoice != new SameNameHandDiscardPaidEvent(f.Id, target, paid.CardId, r.SequenceBefore, r.SequenceAfter, batch)) return false;
            var records = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
            return records is [var movement] && movement.CardId == paid.CardId && movement.CardKind == paid.PrintedKind &&
                movement.From == paid.From && movement.To == CardLocation.DiscardPile && movement.Reason.Value == SameNameHandDiscardReason(f) &&
                (r.Stage == SameNameHandStage.Complete ? f.PendingMovementContinuation is null :
                    f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == target);
        }
        return r.Stage is SameNameHandStage.DamageIssued or SameNameHandStage.Complete && r.DamageIssued && r.PaidMaterial is null &&
            r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null && payments.Length == 0 && f.PendingMovementContinuation is null &&
            damage is [var issued] && issued == new SameNameHandDamageIssuedEvent(f.Id, f.OwnerSeat, target, 1);
    }

    private bool SameNameHandFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.SameNameHandDiscardOrDamage is not { } r || !ValidSameNameHandReceipt(f)) return false;
        if (r.Stage == SameNameHandStage.DiscardChildren && r.PaidMaterial is { } paid && child is CardsMovedTriggerWindowFrame moved)
            return f.PendingMovementContinuation is not null && moved.Id == r.BatchId && moved.Batch.Id == moved.Id &&
                moved.ResumeProgramFrameId is null && moved.Batch.ParentFrameId == f.Id && moved.Batch.AwaitingProgramFrameId == f.Id &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements is [var movement] && _cardMovements.Contains(movement) && movement.Sequence > r.SequenceBefore && movement.Sequence <= r.SequenceAfter &&
                movement.CardId == paid.CardId && movement.From == paid.From && movement.To == CardLocation.DiscardPile && movement.Reason.Value == SameNameHandDiscardReason(f);
        return r.Stage == SameNameHandStage.DamageIssued && r.DamageIssued && f.AttackAttempt is not null &&
            DyingSuitsStructuralEdge(f, child);
    }
    private bool SameNameHandStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame f && SameNameHandFirstChild(f, child) ||
        child is ProgramSkillFrame { SameNameHandDiscardOrDamage: not null } observer && observer.WindowContext?.ParentFrameId == parent.Id && ValidSameNameHandReceipt(observer);

    private bool IsSameNameHandDiscardOrDamageChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.SameNameHandDiscardOrDamage is not { Stage: SameNameHandStage.ChoosingTarget or SameNameHandStage.ChoosingPayment } r ||
            !ValidSameNameHandReceipt(f) || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != (r.Stage == SameNameHandStage.ChoosingTarget ? f.OwnerSeat : r.TargetSeat) || decision.TargetSeat != decision.PlayerSeat ||
            decision.SourceSeat != f.OwnerSeat || decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 || decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = SameNameHandChoices(f);
        return decision.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards).Distinct()) &&
            decision.ValidTargetSeats.SequenceEqual(expected.SelectMany(c => c.Targets).Distinct()) &&
            decision.Choices.Count == expected.Count && decision.Choices.Zip(expected).All(p => SameNameHandChoicesEqual(p.First, p.Second));
    }
    private void AssertSameNameHandDiscardOrDamage(ProgramSkillFrame f)
    {
        if (f.SameNameHandDiscardOrDamage is null)
        {
            // The overwhelmingly common old binding never scans event history.
            if (f.TriggerId is null || _contentRegistry.GetSkill(f.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == f.TriggerId)?.Effects
                    .Any(e => e.Op == SkillProgramEffectOp.OfferSameNameHandDiscardOrDamage) != true) return;
            if (CompleteProgramEventHistory().OfType<SameNameHandStartedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("An issued same-name hand demand lost its owning receipt.");
            return;
        }
        if (!ValidSameNameHandReceipt(f)) throw new InvalidOperationException("A same-name demand lost its completed action, original actor, private choice or once-paid receipt.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !SameNameHandFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("A same-name hand demand retained an unrelated first native child.");
        if (index == _resolutionStack.Count - 1 && _pendingDecision is { } prompt &&
            f.SameNameHandDiscardOrDamage.Stage is SameNameHandStage.ChoosingTarget or SameNameHandStage.ChoosingPayment &&
            !IsSameNameHandDiscardOrDamageChoice(f, prompt)) throw new InvalidOperationException("A same-name demand lost its exact private published choice.");
    }

    private bool SameNameHandObserverSuffix(int index)
    {
        for (var child = index + 2; child < _resolutionStack.Count; child++)
        {
            var parent = _resolutionStack[child - 1]; var current = _resolutionStack[child];
            if (current is ProgramLifecycleTriggerWindowFrame skills && parent is ProgramSkillFrame changed &&
                skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == changed.Id &&
                skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count) continue;
            if (!RoundGainedEquipmentDrawStructuralEdge(parent, current) && !JoinedTrickDamageRewardStructuralEdge(parent, current) && !PairedColorDispositionStructuralEdge(parent, current) && !SameNameHandStructuralEdge(parent, current) && !ResponseCompletionStructuralEdge(parent, current) && !CardSupplyCompletionStructuralEdge(parent, current) &&
                !CompletedUndamagedTargetRevealStructuralEdge(parent, current) && !RecipientCategoryMarkStructuralEdge(parent, current) && !OffTurnUsedCardGiftStructuralEdge(parent, current) && !OriginalHandEntityStructuralEdge(parent, current) &&
                !OutsidePhaseDrawDiscardStructuralEdge(parent, current) && !DyingSuitsStructuralEdge(parent, current) &&
                !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) return false;
            if (current is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                 IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                 PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying) ||
                 TieredRoundZeroDyingRescueRide(child, dying) || DrawFundedDistinctBasicDyingRescueRide(child, dying))) break;
        }
        return true;
    }
    private ProgramSkillFrame? SameNameHandObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame root && SameNameHandFirstChild(root, _resolutionStack[index + 1]) &&
                SameNameHandObserverSuffix(index)) return root;
        return null;
    }
    private ProgramCardTriggerWindowFrame? ResponseCompletionObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramCardTriggerWindowFrame { ResponseCompletion: not null } root &&
                ValidResponseCompletionWindow(root) && ResponseCompletionFirstChild(root, _resolutionStack[index + 1]) && SameNameHandObserverSuffix(index)) return root;
        return null;
    }
    private bool HasSameNameHandCardObserver(long parentFrameId) =>
        SameNameHandObserverRoot() is { } root && root.SameNameHandDiscardOrDamage!.OriginalParentFrameId == parentFrameId ||
        ResponseCompletionObserverRoot() is { } response && response.ParentFrameId == parentFrameId ||
        CardSupplyCompletionObserverRoot() is { } supply && supply.ParentFrameId == parentFrameId;
    private bool IsSameNameHandDying() => ActiveDying is { } dying &&
        (SameNameHandObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id) ||
         ResponseCompletionObserverRoot() is { } response && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == response.Id) ||
         CardSupplyCompletionObserverRoot() is { } supply && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == supply.Id));
    private bool HasSameNameHandDamageObserver(long windowId) => _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) &&
        (SameNameHandObserverRoot() is not null || ResponseCompletionObserverRoot() is not null || CardSupplyCompletionObserverRoot() is not null);
    private bool AllowsSameNameHandNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id) return false;
        if (observer.SameNameHandDiscardOrDamage is { Stage: SameNameHandStage.DamageIssued, DamageIssued: true, TargetSeat: { } issuedTarget } &&
            ValidSameNameHandReceipt(observer)) return target == issuedTarget && amount == 1 && source is null && nature is null;
        if (observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            SameNameHandObserverRoot() is null && ResponseCompletionObserverRoot() is null && CardSupplyCompletionObserverRoot() is null) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvanceSameNameHandSubtree()
    {
        if (_pendingDecision is not null || SameNameHandObserverRoot() is null && ResponseCompletionObserverRoot() is null && CardSupplyCompletionObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame before &&
                    (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
