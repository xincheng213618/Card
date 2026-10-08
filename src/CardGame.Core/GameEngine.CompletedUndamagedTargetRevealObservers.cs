namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactCompletedUndamagedParent(ProgramSkillFrame f, ProgramSkillWindowContext context, CardUseFrame use)
    {
        if (context.CardUse is not { } original) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == f.Id);
        if (index < 2 || _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window ||
            window.Id != context.ParentFrameId || window.ParentFrameId != use.Id || !window.Activated ||
            window.Continuation is not (ProgramCardContinuation.CompletedCard or ProgramCardContinuation.CompletedSlash) ||
            window.CompletedResponseReturn is not null || GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCompleted ||
            window.Action.Type != CardActionType.Use || window.Action.ActionId != use.Action?.ActionId ||
            _resolutionStack[index - 2].Id != use.Id || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count)
            return false;
        var c = window.Candidates[window.CandidateIndex];
        return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && CreateCardActionProgramContext(window, c) == context &&
            original.ParentCardUseFrameId == use.Id && original.CardActionId == window.Action.ActionId &&
            HasExactAcceptedCompletedUndamagedUse(use, window.Action);
    }
    private bool ValidCompletedUndamagedTargetRevealReceipt(ProgramSkillFrame f)
    {
        if (f.CompletedUndamagedTargetReveal is not { } r || f.WindowContext is not { } context || f.TriggerId is null ||
            r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) || r.WindowFrameId != context.ParentFrameId ||
            CompletedUndamagedUse(context) is not { Action: { } action } use || !ExactCompletedUndamagedParent(f, context, use) ||
            use.Id != r.CardUseFrameId || action.ActionId != r.CardActionId || use.CardKind != r.EffectiveKind || use.SourceSeat != f.OwnerSeat ||
            !r.FinalTargets.SequenceEqual(use.TargetSeats) || r.EligibleTargets.Count == 0 ||
            r.EligibleTargets.Distinct().Count() != r.EligibleTargets.Count ||
            r.EligibleTargets.Any(s => !r.FinalTargets.Contains(s))) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Trigger is not { Window: SkillProgramTriggerWindow.CardUseCompleted, Optional: true, IncludeResponseUses: false,
                OwnerRelation: SkillProgramCardActionOwnerRelation.Actor } trigger ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor,
                Target: SkillProgramEffectTarget.Owner, Amount: 3, Condition.Kind: SkillProgramConditionKind.Always }] ||
            plan.Instructions.Count != 1 || plan.Instructions[0].Op != SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
            e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.OwnerSeat == f.OwnerSeat &&
            e.Window == SkillProgramTriggerWindow.CardUseCompleted) != 1) return false;
        var damage = history.OfType<CompletedUndamagedUseDamageRecordedEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        if (damage.Select(e => e.DamageFrameId).Distinct().Count() != damage.Length ||
            damage.Any(e => e.Amount <= 0 || !IsValidPlayerSeat(e.SourceSeat) || !IsValidPlayerSeat(e.TargetSeat) ||
                e.ActorSeat != use.SourceSeat || e.EffectiveKind != use.CardKind ||
                e.CardActionId is { } id && id != action.ActionId ||
                history.OfType<DamageRequestedEvent>().Count(d => d.ResolutionId == e.DamageFrameId && d.SourceSeat == e.SourceSeat &&
                    d.TargetSeat == e.TargetSeat && d.Amount == e.Amount && d.Nature == e.Nature && d.SourceLess == e.SourceLess &&
                    d.SourceCard == e.EffectiveKind) != 1) || r.EligibleTargets.Any(s => damage.Any(e => e.TargetSeat == s))) return false;
        foreach (var fact in damage)
        {
            var requested = Array.FindIndex(history, e => e is DamageRequestedEvent d && d.ResolutionId == fact.DamageFrameId);
            if (history.Skip(requested + 1).TakeWhile(e => e is not DamageRequestedEvent).OfType<DamageAppliedEvent>().FirstOrDefault() is not { } applied ||
                applied.SourceSeat != fact.SourceSeat || applied.TargetSeat != fact.TargetSeat || applied.Amount != fact.Amount ||
                applied.Nature != fact.Nature || applied.SourceLess != fact.SourceLess) return false;
        }
        if (history.OfType<CompletedUndamagedTargetRevealStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.WindowFrameId != r.WindowFrameId ||
            start.CardUseFrameId != r.CardUseFrameId || start.CardActionId != r.CardActionId) return false;
        var selections = history.OfType<CompletedUndamagedTargetRevealSelectionIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var movements = history.OfType<CompletedUndamagedTargetRevealMovementIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completions = history.OfType<CompletedUndamagedTargetRevealCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var reveals = history.OfType<ProgramCardsRevealedEvent>().Where(e => e.FrameId == f.Id && e.Bind == CompletedUndamagedRevealBind).ToArray();
        if (r.Stage == CompletedUndamagedTargetRevealStage.ChoosingTarget)
            return r.TargetSeat is null && r.TargetHand.Count == 0 && r.SelectedSlots.Count == 0 && !r.SelectionIssued &&
                r.RevealedMaterials.Count == 0 && r.PaidMaterials.Count == 0 && !r.SameColor && !r.MovementIssued &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null && f.PendingMovementContinuation is null &&
                selections.Length == 0 && movements.Length == 0 && completions.Length == 0 && reveals.Length == 0;
        if (r.TargetSeat is not { } target || !r.EligibleTargets.Contains(target) ||
            r.TargetHand.Select(m => m.CardId).Distinct().Count() != r.TargetHand.Count ||
            r.TargetHand.Where((m, slot) => m.Slot != slot || m.CardId <= 0 || !Enum.IsDefined(m.PrintedKind)).Any() ||
            r.SelectedSlots.Distinct().Count() != r.SelectedSlots.Count || r.SelectedSlots.Count > Math.Min(3, r.TargetHand.Count) ||
            r.SelectedSlots.Any(s => s < 0 || s >= r.TargetHand.Count)) return false;
        if (r.Stage == CompletedUndamagedTargetRevealStage.ChoosingCards)
            return !r.SelectionIssued && r.SelectedSlots.Count < Math.Min(3, r.TargetHand.Count) + (r.TargetHand.Count == 0 ? 1 : 0) &&
                r.RevealedMaterials.Count == 0 && r.PaidMaterials.Count == 0 && !r.SameColor && !r.MovementIssued &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null && f.PendingMovementContinuation is null &&
                selections.Length == 0 && movements.Length == 0 && completions.Length == 0 && reveals.Length == 0;
        if (!r.SelectionIssued || selections is not [var selection] || selection.TargetSeat != target ||
            selection.SelectedCount != r.SelectedSlots.Count || selection.RevealedCount != r.RevealedMaterials.Count || selection.SameColor != r.SameColor ||
            r.RevealedMaterials.Select(m => m.CardId).Distinct().Count() != r.RevealedMaterials.Count ||
            r.RevealedMaterials.Any(m => !r.SelectedSlots.Contains(m.Slot) || r.TargetHand[m.Slot].CardId != m.CardId ||
                r.TargetHand[m.Slot].PrintedKind != m.PrintedKind || !Enum.IsDefined(m.EffectiveSuit) || m.EffectiveColor != CompletedUndamagedColor(m.EffectiveSuit)) ||
            r.SameColor != (r.RevealedMaterials.Count > 0 && r.RevealedMaterials.All(m => m.EffectiveColor == r.RevealedMaterials[0].EffectiveColor)) ||
            r.PaidMaterials.Select(m => m.CardId).Distinct().Count() != r.PaidMaterials.Count ||
            r.PaidMaterials.Any(m => !r.RevealedMaterials.Contains(m)) || !r.SameColor && r.PaidMaterials.Count > 0 ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > CompletedUndamagedMovementSequence) return false;
        if (r.RevealedMaterials.Count == 0 ? reveals.Length != 0 : reveals is not [var shown] || shown.SkillId != f.SkillId ||
            shown.BindingId != GetProgramBindingId(f) || shown.OwnerSeat != f.OwnerSeat ||
            !shown.Cards.Select(c => c.Id).SequenceEqual(r.RevealedMaterials.Select(m => m.CardId)) ||
            shown.Cards.Zip(r.RevealedMaterials).Any(p => p.First.Kind != p.Second.PrintedKind || p.First.Suit != p.Second.EffectiveSuit)) return false;
        if (r.PaidMaterials.Count == 0)
            return r.Stage == CompletedUndamagedTargetRevealStage.Complete && !r.MovementIssued && r.BatchId is null &&
                r.SequenceAfter == r.SequenceBefore && f.PendingMovementContinuation is null && movements.Length == 0 &&
                completions is [var empty] && empty.SelectedCount == r.SelectedSlots.Count && empty.ActualDiscardCount == 0;
        if (!r.MovementIssued || r.BatchId is not { } batchId || movements is not [var paid] ||
            paid.TargetSeat != target || paid.ActualCount != r.PaidMaterials.Count || paid.SequenceBefore != r.SequenceBefore ||
            paid.SequenceAfter != r.SequenceAfter || paid.BatchId != batchId) return false;
        var records = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        if (records.Length != r.PaidMaterials.Count || !records.Select(m => m.CardId).SequenceEqual(r.PaidMaterials.Select(m => m.CardId)) ||
            records.Any(m => m.From != CardLocation.Hand(target) || m.To != CardLocation.DiscardPile ||
                m.Reason.Value != CompletedUndamagedDiscardReason(f) || m.CardKind != r.PaidMaterials.Single(c => c.CardId == m.CardId).PrintedKind)) return false;
        return r.Stage == CompletedUndamagedTargetRevealStage.Complete ? f.PendingMovementContinuation is null &&
                completions is [var complete] && complete.SelectedCount == r.SelectedSlots.Count && complete.ActualDiscardCount == records.Length :
            r.Stage == CompletedUndamagedTargetRevealStage.MovementChildren && completions.Length == 0 &&
                f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending && pending.SubjectSeat == target;
    }
    private bool CompletedUndamagedTargetRevealFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidCompletedUndamagedTargetRevealReceipt(f) || f.CompletedUndamagedTargetReveal is not
            { Stage: CompletedUndamagedTargetRevealStage.MovementChildren, MovementIssued: true, TargetSeat: { } target, BatchId: { } batch } r ||
            child is not CardsMovedTriggerWindowFrame moved) return false;
        return moved.ResumeProgramFrameId is null && moved.Id == batch && moved.Batch.Id == batch && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
            moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count == r.PaidMaterials.Count &&
            moved.Batch.Movements.Select(m => m.CardId).SequenceEqual(r.PaidMaterials.Select(m => m.CardId)) &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                m.From == CardLocation.Hand(target) && m.To == CardLocation.DiscardPile && m.Reason.Value == CompletedUndamagedDiscardReason(f) &&
                m.CardKind == r.PaidMaterials.Single(c => c.CardId == m.CardId).PrintedKind);
    }
    private bool IsCompletedUndamagedTargetRevealChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.CompletedUndamagedTargetReveal is not { Stage: CompletedUndamagedTargetRevealStage.ChoosingTarget or
                CompletedUndamagedTargetRevealStage.ChoosingCards } || !ValidCompletedUndamagedTargetRevealReceipt(f) ||
            decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate || decision.PlayerSeat != f.OwnerSeat ||
            decision.SourceSeat != f.OwnerSeat || decision.TargetSeat is not null || decision.ValidCardIds.Count != 0 ||
            decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 || decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var choices = CompletedUndamagedTargetRevealChoices(f);
        return decision.ValidTargetSeats.SequenceEqual(choices.SelectMany(c => c.Targets).Distinct()) && decision.Choices.Count == choices.Count &&
            decision.Choices.Zip(choices).All(p => CompletedUndamagedChoiceEquals(p.First, p.Second));
    }
    private void AssertCompletedUndamagedTargetReveal(ProgramSkillFrame f)
    {
        if (f.CompletedUndamagedTargetReveal is null)
        {
            // Old bindings in a full catalog do not scan the whole event history.
            if (f.TriggerId is null || !ProgramInstructionResolver.Default.Features(GetProgramTrigger(f))
                    .HasOperation(SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor)) return;
            if (CompleteProgramEventHistory().OfType<CompletedUndamagedTargetRevealStartedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("An issued completed-target reveal lost its owning receipt.");
            return;
        }
        if (!ValidCompletedUndamagedTargetRevealReceipt(f))
            throw new InvalidOperationException("Completed-target reveal lost its exact completed Use, whole-use damage exclusion, opaque selection or native paid invoice.");
        var index = _resolutionStack.FindIndex(item => item.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !CompletedUndamagedTargetRevealFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Completed-target reveal retained an unrelated first native child.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.CompletedUndamagedTargetReveal.Stage is
                CompletedUndamagedTargetRevealStage.ChoosingTarget or CompletedUndamagedTargetRevealStage.ChoosingCards &&
            (_pendingDecision is not { } decision || !IsCompletedUndamagedTargetRevealChoice(f, decision)))
            throw new InvalidOperationException("Completed-target reveal lost its exact private opaque prompt.");
    }
    private bool CompletedUndamagedTargetRevealStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame root && CompletedUndamagedTargetRevealFirstChild(root, child)) return true;
        return parent is ProgramCardTriggerWindowFrame window && child is ProgramSkillFrame { CompletedUndamagedTargetReveal: { } r } observer &&
            r.WindowFrameId == window.Id && ValidCompletedUndamagedTargetRevealReceipt(observer);
    }
    private ProgramSkillFrame? CompletedUndamagedTargetRevealObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !CompletedUndamagedTargetRevealFirstChild(root, _resolutionStack[index + 1])) continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                    changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OriginalHandEntityStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OutsidePhaseDrawDiscardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CompletedUndamagedTargetRevealStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !RecipientCategoryMarkStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !OffTurnUsedCardGiftStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !SameNameHandStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CardSupplyCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying) ||
                     TieredRoundZeroDyingRescueRide(child, dying) || DrawFundedDistinctBasicDyingRescueRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsCompletedUndamagedTargetRevealDying() => ActiveDying is { } dying && CompletedUndamagedTargetRevealObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasCompletedUndamagedTargetRevealDamageObserver(long windowId) =>
        _resolutionStack.Any(f => f.Id == windowId && f is DamageTriggerWindowFrame) && CompletedUndamagedTargetRevealObserverRoot() is not null;
    private bool AllowsCompletedUndamagedTargetRevealNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            CompletedUndamagedTargetRevealObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvanceCompletedUndamagedTargetRevealSubtree()
    {
        if (_pendingDecision is not null || CompletedUndamagedTargetRevealObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
