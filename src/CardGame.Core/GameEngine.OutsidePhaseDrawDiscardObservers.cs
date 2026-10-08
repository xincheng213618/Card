namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidOutsidePhaseDrawDiscardReceipt(ProgramSkillFrame f)
    {
        if (f.OutsidePhaseDrawDiscard is not { } r || f.WindowContext is not { MovementBatch: { } batch } context ||
            !IsOutsidePhaseOperation(r.Op) || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || context.ParentFrameId != r.WindowFrameId || batch.Id != r.OriginalBatchId ||
            !Enum.IsDefined(r.Stage) || r.OriginalMovementIndex < 0 || r.OriginalMovementIndex >= batch.Movements.Count) return false;
        var parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == r.WindowFrameId);
        if (parent is null || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Batch.Id != batch.Id || !parent.Batch.Movements.SequenceEqual(batch.Movements) ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        var trigger = GetProgramTrigger(f);
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (trigger.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Amount: 1,
                Condition.Kind: SkillProgramConditionKind.Always } effect] || effect.Op != r.Op ||
            plan.Instructions.Count != 1 || plan.Instructions[0].Op != r.Op ||
            trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerBatch || trigger.Optional) return false;
        var draw = r.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
        var candidate = parent.Candidates[parent.CandidateIndex];
        if (draw)
        {
            var indexes = MatchingOutsideDrawIndexes(batch, candidate);
            if (context.Window != SkillProgramTriggerWindow.CardsGained || r.OriginalDraw is not { } proof ||
                batch.NativeDrawInvocation is not { } native || proof.InvocationId != native.InvocationId ||
                !ValidNativeDrawInvocation(proof) || !proof.Materials.SequenceEqual(native.Materials) ||
                proof.LastBatchId != r.OriginalBatchId || proof.Phase != r.OriginalPhase ||
                indexes is not [var index] || index != r.OriginalMovementIndex || r.OriginalDiscards.Count != 0 ||
                r.EligibleMaterials.Count != 0 || r.EligibleTargets.Count == 0 ||
                r.EligibleTargets.Distinct().Count() != r.EligibleTargets.Count || r.EligibleTargets.Any(s => !IsValidPlayerSeat(s))) return false;
        }
        else
        {
            var indexes = MatchingOutsideDiscardIndexes(batch, candidate);
            var expected = indexes.Select(i => new DiscardRecoveryEntity(batch.Movements[i].CardId, batch.Movements[i].Sequence,
                GetProgramDiscardSource(batch.Movements[i])!.Value));
            if (context.Window != SkillProgramTriggerWindow.DiscardPileReceived || context.MovementIndex != r.OriginalMovementIndex ||
                context.SourceSeat != f.OwnerSeat || context.TargetSeat != f.OwnerSeat || r.OriginalDraw is not null ||
                batch.DiscardRecoveryPhase != r.OriginalPhase || indexes.Length == 0 || indexes[0] != r.OriginalMovementIndex ||
                !r.OriginalDiscards.SequenceEqual(expected) || r.EligibleTargets.Count != 0 || r.EligibleMaterials.Count == 0 ||
                r.EligibleMaterials.Select(m => m.CardId).Distinct().Count() != r.EligibleMaterials.Count ||
                r.EligibleMaterials.Any(m => m.CardId <= 0 || !Enum.IsDefined(m.PrintedKind) || m.Slot < 0 ||
                    m.From.OwnerSeat is not { } seat || !IsValidPlayerSeat(seat) || seat == f.OwnerSeat ||
                    m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment))) return false;
        }
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<OutsidePhaseDrawDiscardStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start.Source != r.Source || start.GameplayHash != r.GameplayHash || start.Op != r.Op ||
            start.WindowFrameId != r.WindowFrameId || start.OriginalBatchId != r.OriginalBatchId ||
            start.NativeDrawInvocationId != r.OriginalDraw?.InvocationId) return false;
        var issues = history.OfType<OutsidePhaseDrawDiscardMovementIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completions = history.OfType<OutsidePhaseDrawDiscardCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == OutsidePhaseDrawDiscardStage.Choosing)
            return !r.MovementIssued && r.ParticipantSeat is null && r.PaidMaterial is null && r.ActualCount == 0 &&
                r.SequenceBefore == 0 && r.SequenceAfter == 0 && r.BatchId is null &&
                f.PendingMovementContinuation is null && issues.Length == 0 && completions.Length == 0;
        if (!r.MovementIssued || r.ParticipantSeat is not { } participant || !IsValidPlayerSeat(participant) ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore || r.SequenceAfter > OutsidePhaseMovementSequence ||
            issues is not [var issue] || issue.Op != r.Op || issue.ParticipantSeat != participant ||
            issue.ActualCount != r.ActualCount || issue.SequenceBefore != r.SequenceBefore || issue.SequenceAfter != r.SequenceAfter ||
            issue.BatchId != r.BatchId || issue.PaidMaterial != r.PaidMaterial) return false;
        var movements = _cardMovements.Where(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter).ToArray();
        if (draw)
        {
            if (r.PaidMaterial is not null || r.BatchId is not null || !r.EligibleTargets.Contains(participant) || r.ActualCount is < 0 or > 1 ||
                movements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(participant) &&
                    m.Reason.Value == OutsidePhaseMovementReason(f, true)) != r.ActualCount ||
                movements.Any(m => !(m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(participant) &&
                    m.Reason.Value == OutsidePhaseMovementReason(f, true) || m.From == CardLocation.DiscardPile &&
                    m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle))) return false;
            var native = history.OfType<NativeDrawInvocationRecordedEvent>().Where(e => e.ParentFrameId == f.Id &&
                e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter && e.OwnerSeat == participant &&
                e.Reason.Value == OutsidePhaseMovementReason(f, true) && e.DirectProducer == r.Source).ToArray();
            if (r.ActualCount == 1 ? native is not [{ ActualCount: 1, Requested: 1 }] : native.Length != 0) return false;
        }
        else
        {
            if (participant == f.OwnerSeat || r.PaidMaterial is not { } material || !r.EligibleMaterials.Contains(material) ||
                material.From.OwnerSeat != participant || r.ActualCount != 1 || r.BatchId is null || movements is not [var paid] ||
                paid.CardId != material.CardId || paid.CardKind != material.PrintedKind || paid.From != material.From ||
                paid.To != (material.IsGeneralWeapon && material.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile) ||
                paid.Reason.Value != OutsidePhaseMovementReason(f, false)) return false;
        }
        return r.Stage == OutsidePhaseDrawDiscardStage.Complete
            ? f.PendingMovementContinuation is null && completions is [var complete] && complete.ActualCount == r.ActualCount
            : completions.Length == 0 && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending &&
                pending.SubjectSeat == participant;
    }
    private bool OutsidePhaseDrawDiscardFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidOutsidePhaseDrawDiscardReceipt(f) || f.OutsidePhaseDrawDiscard is not
            { MovementIssued: true, Stage: OutsidePhaseDrawDiscardStage.MovementChildren, ParticipantSeat: { } participant } r) return false;
        var draw = r.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
        bool PaidEquipment(CardKind kind) => !draw && r.PaidMaterial is { From.Zone: CardZoneKind.Equipment } material &&
            material.PrintedKind == kind && _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                m.CardId == material.CardId && m.From == material.From && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == OutsidePhaseMovementReason(f, false));
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == f.Id &&
                moved.Batch.AwaitingProgramFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                        (draw ? m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(participant) &&
                            m.Reason.Value == OutsidePhaseMovementReason(f, true) || m.From == CardLocation.DiscardPile &&
                            m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle :
                            moved.Batch.Id == r.BatchId && r.PaidMaterial is { } material && m.CardId == material.CardId &&
                            m.CardKind == material.PrintedKind && m.From == material.From &&
                            m.To == (material.IsGeneralWeapon && material.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile) &&
                            m.Reason.Value == OutsidePhaseMovementReason(f, false)) ||
                     !draw && PaidEquipment(CardKind.WoodenOx) && moved.Batch.ParentBatchId == r.BatchId &&
                        m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(participant) &&
                        m.To == CardLocation.DiscardPile));
        if (!draw && r.PaidMaterial?.From.Zone == CardZoneKind.Equipment && child is ProgramLifecycleTriggerWindowFrame skills &&
            skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (!PaidEquipment(CardKind.SilverLion)) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == participant &&
                hp.Change.TargetSeat == participant && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, f) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == participant &&
            recovery.Attempt.TargetSeat == participant && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.MoveReason?.Value == OutsidePhaseMovementReason(f, false);
    }
    private bool IsOutsidePhaseDrawDiscardChoice(ProgramSkillFrame f, PendingDecision decision)
    {
        if (f.OutsidePhaseDrawDiscard is not { Stage: OutsidePhaseDrawDiscardStage.Choosing } ||
            !ValidOutsidePhaseDrawDiscardReceipt(f) || decision.Kind != DecisionKind.ProgramTrigger || !decision.IsPrivate ||
            decision.PlayerSeat != f.OwnerSeat || decision.SourceSeat != f.OwnerSeat || decision.TargetSeat is not null ||
            decision.ValidContentIds.Count != 0 || decision.RequiredCardCount != 0 || decision.SkillPrompt?.SkillId != f.SkillId) return false;
        var expected = OutsidePhaseDrawDiscardChoices(f);
        return decision.ValidCardIds.SequenceEqual(expected.SelectMany(c => c.Cards).Distinct()) &&
            decision.ValidTargetSeats.SequenceEqual(expected.SelectMany(c => c.Targets).Distinct()) && decision.Choices.Count == expected.Count &&
            decision.Choices.Zip(expected).All(pair => pair.First.Id == pair.Second.Id && pair.First.Cards.SequenceEqual(pair.Second.Cards) &&
                pair.First.Targets.SequenceEqual(pair.Second.Targets) && pair.First.Parameters.OrderBy(x => x.Key).SequenceEqual(pair.Second.Parameters.OrderBy(x => x.Key)));
    }
    private void AssertOutsidePhaseDrawDiscard(ProgramSkillFrame f)
    {
        if (f.OutsidePhaseDrawDiscard is null) return;
        if (!ValidOutsidePhaseDrawDiscardReceipt(f))
            throw new InvalidOperationException("Outside-phase draw/discard lost its real producer, actual phase, original candidate or once-issued native movement receipt.");
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !OutsidePhaseDrawDiscardFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Outside-phase draw/discard retained an unrelated first native child.");
        if (ReferenceEquals(f, _resolutionStack.LastOrDefault()) && f.OutsidePhaseDrawDiscard.Stage == OutsidePhaseDrawDiscardStage.Choosing &&
            (_pendingDecision is not { } decision || !IsOutsidePhaseDrawDiscardChoice(f, decision)))
            throw new InvalidOperationException("Outside-phase draw/discard lost its exact private forced choice.");
    }
    private bool OutsidePhaseDrawDiscardStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (parent is ProgramSkillFrame f && OutsidePhaseDrawDiscardFirstChild(f, child)) return true;
        return parent is CardsMovedTriggerWindowFrame window && child is ProgramSkillFrame { OutsidePhaseDrawDiscard: { } r } observer &&
            r.WindowFrameId == window.Id && observer.WindowContext?.ParentFrameId == window.Id &&
            window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
            MountObserverCandidateMatches(observer, window.Candidates[window.CandidateIndex]) && ValidOutsidePhaseDrawDiscardReceipt(observer);
    }
    private ProgramSkillFrame? OutsidePhaseDrawDiscardObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !OutsidePhaseDrawDiscardFirstChild(root, _resolutionStack[index + 1])) continue;
            var aligned = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
            {
                if (_resolutionStack[child] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[child - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                    changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !SameNameHandStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CompletedUndamagedTargetRevealStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !RecipientCategoryMarkStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) && !OffTurnUsedCardGiftStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !CardSupplyCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OriginalHandEntityStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !OutsidePhaseDrawDiscardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                    !HalfHandPaidDamageObserverEdge(child) && !PaidTargetObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && child + 1 < _resolutionStack.Count &&
                    ((IsOriginalDyingSuspendedByDyingSuits(dying) || IsOriginalDyingSuspendedByRecipientCategoryMark(dying)) || IsOriginalDyingSuspendedByOwnedDeathBenefit(dying) ||
                     IsPaidHandRepaymentProgramAlcoholRide(child, dying) || IsPaidHandRepaymentRescueRide(child, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(child, dying) || PaidObserverDamageVirtualAlcoholRide(child, dying))) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsOutsidePhaseDrawDiscardDying() => ActiveDying is { } dying && OutsidePhaseDrawDiscardObserverRoot() is { } root &&
        _resolutionStack.FindIndex(frame => frame.Id == dying.Id) > _resolutionStack.FindIndex(frame => frame.Id == root.Id);
    private bool HasOutsidePhaseDrawDiscardDamageObserver(long windowId) => _resolutionStack.Any(frame =>
        frame.Id == windowId && frame is DamageTriggerWindowFrame) && OutsidePhaseDrawDiscardObserverRoot() is not null;
    private bool AllowsOutsidePhaseDrawDiscardNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            OutsidePhaseDrawDiscardObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }
    private bool TryAdvanceOutsidePhaseDrawDiscardSubtree()
    {
        if (_pendingDecision is not null || OutsidePhaseDrawDiscardObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(frame => frame is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    frame is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
