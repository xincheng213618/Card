namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidActualHandGain(ProgramSkillFrame f)
    {
        if (f.ActualHandGain is not { } r || r.InstructionIndex != f.InstructionIndex ||
            ActualHandGainPausedEffect(f)?.Op != r.Operation || r.Source != ActualHandGainSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash || r.ActualTurn != _turnNumber ||
            !IsValidPlayerSeat(r.ActualTurnOwner) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.PaidIds.Distinct().Count() != r.PaidIds.Count || r.Gains.Select(g => g.CardId).Distinct().Count() != r.Gains.Count ||
            f.WindowContext is not { } context || context.ParentFrameId != r.OriginalParentId) return false;
        if (r.Operation == SkillProgramEffectOp.DrawAfterActualOwnHandGain)
        {
            var parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault(w => w.Id == r.OriginalParentId);
            if (parent is null || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
                !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) || context.MovementBatch is not { } batch ||
                batch.Id != parent.Batch.Id || !batch.Movements.SequenceEqual(parent.Batch.Movements) || r.OriginalBatchId != parent.Batch.Id ||
                parent.Batch.TurnNumber != r.ActualTurn || parent.Batch.MovementTiming is not { } timing || timing.ActualTurnOwnerSeat != f.OwnerSeat ||
                timing.Phase is TurnPhase.NotStarted or TurnPhase.Finished || r.Gains.Count == 0 ||
                !HasRecordedActualOwnHandGain(parent.Candidates[parent.CandidateIndex], parent.Batch) ||
                r.ActualTurnOwner != f.OwnerSeat || r.Category is not null || r.RecipientSeat != -1 ||
                IsActualHandGainOwnDrawBatch(parent.Batch, f.OwnerSeat, f.SkillId, f.GameplayHash) ||
                !r.Gains.SequenceEqual(ActualHandGainEntities(parent.Batch, f.OwnerSeat))) return false;
        }
        else if (r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains)
        {
            var ended = _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(w => w.Id == r.OriginalParentId);
            if (ended is null || !IsActualAfterTurnEndedParent(ended) || !MountObserverCandidateMatches(f, AfterTurnEndedCandidate(ended)) ||
                ended.OwnerSeat != r.ActualTurnOwner || ended.TurnNumber != r.ActualTurn || ended.OwnerSeat == f.OwnerSeat ||
                r.OriginalBatchId != 0 || r.Category is not null || r.RecipientSeat != -1 ||
                !r.Gains.SequenceEqual(ForeignHandGainRoster(f.OwnerSeat, f.SkillId, f.GameplayHash, r.ActualTurn, r.ActualTurnOwner)) ||
                r.Gains.Any(g => !_cardMovements.Any(m => m.Sequence == g.GainSequence && m.CardId == g.CardId &&
                    m.TurnNumber == r.ActualTurn && m.To == CardLocation.Hand(f.OwnerSeat) && ActualHandGainOriginalSource(m) != CardLocation.Hand(f.OwnerSeat))) ||
                r.PaidIds.Any(id => !r.Gains.Any(g => g.CardId == id))) return false;
        }
        else if (r.Operation == SkillProgramEffectOp.GiveSameCategoryFromDeck)
        {
            var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.Id == r.OriginalParentId);
            if (parent is null || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
                !MountObserverCandidateMatches(f, ToSharedCandidate(parent.Candidates[parent.CandidateIndex])) ||
                !ExactActualHandGainUse(ToSharedCandidate(parent.Candidates[parent.CandidateIndex]), context, out _, out var use) ||
                r.OriginalCardUseFrameId != use.Id || r.OriginalActionId != parent.Action.ActionId || r.ActualTurnOwner != f.OwnerSeat ||
                r.Category != ActualHandGainCategory(parent.Action.EffectiveKind) || r.Gains.Count != 0 || r.OriginalBatchId != 0 || r.PaidIds.Count > 1 ||
                r.Stage == ActualHandGainProgramStage.MovementChildren && (!IsValidPlayerSeat(r.RecipientSeat) || r.RecipientSeat == f.OwnerSeat)) return false;
        }
        else return false;
        if (r.Stage == ActualHandGainProgramStage.Choosing)
        {
            if (r.Operation != SkillProgramEffectOp.GiveSameCategoryFromDeck || r.Issued || r.PaidIds.Count != 0 || r.Before != 0 || r.After != 0 ||
                r.RecipientSeat != -1 || f.PendingMovementContinuation is not null || SameCategoryGiftUsed(f.OwnerSeat, f.SkillId, r.ActualTurn, r.Category!.Value)) return false;
            return _resolutionStack.LastOrDefault()?.Id != f.Id || _pendingDecision is null ||
                _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p && p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat &&
                AssistedChoicesEqual(p.Choices, ActualHandGainChoices(f));
        }
        if (r.Stage != ActualHandGainProgramStage.MovementChildren || !r.Issued || f.PendingMovementContinuation is not { } pending ||
            !IsActualHandGainMovement(f, ActualHandGainPausedEffect(f), pending)) return false;
        var reason = r.Operation == SkillProgramEffectOp.DrawAfterActualOwnHandGain ? ActualHandGainDrawReason :
            r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains ? ForeignHandCleanupReason : SameCategoryDeckGiftReason;
        var movements = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == reason).ToArray();
        if (!movements.Select(m => m.CardId).SequenceEqual(r.PaidIds) || movements.Any(m =>
            r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains ? m.From != CardLocation.Hand(f.OwnerSeat) || m.To != CardLocation.DiscardPile :
            m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(r.Operation == SkillProgramEffectOp.DrawAfterActualOwnHandGain ? f.OwnerSeat : r.RecipientSeat) ||
            r.Operation == SkillProgramEffectOp.GiveSameCategoryFromDeck && !MatchesSkillProgramCardCategory(m.CardKind, r.Category!.Value))) return false;
        if (r.Operation == SkillProgramEffectOp.DrawAfterActualOwnHandGain)
        {
            var facts = CompleteProgramEventHistory().OfType<ActualHandGainDrawPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
            return r.PaidIds.Count <= 1 && facts is [var e] && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
                e.OriginalBatchId == r.OriginalBatchId && e.ActualTurn == r.ActualTurn && e.ActualTurnOwner == r.ActualTurnOwner &&
                e.Before == r.Before && e.After == r.After && e.ActualCount == r.PaidIds.Count;
        }
        if (r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains)
        {
            var facts = CompleteProgramEventHistory().OfType<ForeignTurnHandGainsCleanupPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
            return facts is [var e] && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurn == r.ActualTurn &&
                e.ActualTurnOwner == r.ActualTurnOwner && e.CardIds.SequenceEqual(r.PaidIds) && e.Before == r.Before && e.After == r.After;
        }
        var gifts = CompleteProgramEventHistory().OfType<SameCategoryDeckGiftIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        return gifts is [var gift] && gift.Source == r.Source && gift.GameplayHash == r.GameplayHash && gift.ActualTurn == r.ActualTurn &&
            gift.ActualTurnOwner == r.ActualTurnOwner && gift.CardUseFrameId == r.OriginalCardUseFrameId && gift.ActionId == r.OriginalActionId &&
            gift.Category == r.Category && gift.RecipientSeat == r.RecipientSeat && gift.ActualCount == r.PaidIds.Count &&
            gift.Before == r.Before && gift.After == r.After && CompleteProgramEventHistory().OfType<SameCategoryDeckGiftIssuedEvent>().Count(e =>
                e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId && e.ActualTurn == r.ActualTurn && e.Category == r.Category) == 1;
    }
    private void AssertActualHandGain(ProgramSkillFrame f)
    {
        if (f.ActualHandGain is not null && !ValidActualHandGain(f))
            throw new InvalidOperationException("Actual hand gains lost their exact source, native parent, original gain ledger or paid movement.");
    }
    private bool ActualHandGainFirstChild(ProgramSkillFrame root, ResolutionFrame child) => ValidActualHandGain(root) &&
        root.ActualHandGain is { Stage: ActualHandGainProgramStage.MovementChildren } r && root.PendingMovementContinuation is not null &&
        child is CardsMovedTriggerWindowFrame moved && moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null &&
        moved.Batch.AwaitingProgramFrameId == root.Id && moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId &&
        moved.Batch.OriginSkillInstanceId == root.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
        moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
    private ProgramSkillFrame? ActualHandGainObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root || !ActualHandGainFirstChild(root, _resolutionStack[i + 1])) continue;
            var exact = true;
            for (var j = i + 2; j < _resolutionStack.Count; j++)
            {
                if (_resolutionStack[j] is ProgramLifecycleTriggerWindowFrame changed && _resolutionStack[j - 1] is ProgramSkillFrame owner &&
                    changed.Window == SkillProgramTriggerWindow.SkillsChanged && changed.ResumeProgramFrameId == owner.Id &&
                    changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
                if (!(HalfHandPaidDamageObserverEdge(j) || PaidTargetObserverEdge(j) || DyingSuitsStructuralEdge(_resolutionStack[j - 1], _resolutionStack[j])))
                { exact = false; break; }
                if (_resolutionStack[j] is DyingFrame dying && j + 1 < _resolutionStack.Count &&
                    (IsPaidHandRepaymentProgramAlcoholRide(j, dying) || IsPaidHandRepaymentRescueRide(j, dying) ||
                     PolicyCounterspellVirtualAlcoholRide(j, dying) || PaidObserverDamageVirtualAlcoholRide(j, dying) || (TieredRoundZeroDyingRescueRide(j, dying) || DrawFundedDistinctBasicDyingRescueRide(j, dying)))) break;
            }
            if (exact) return root;
        }
        return null;
    }
    private bool AllowsActualHandGainNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            ActiveDying is not null || observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            ActualHandGainObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool IsActualHandGainProgramDying() => ActiveDying is { } dying && ActualHandGainObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasActualHandGainDamageObserver(long window) => _resolutionStack.Any(f => f.Id == window && f is DamageTriggerWindowFrame) && ActualHandGainObserverRoot() is not null;
    private bool TryAdvanceActualHandGainSubtree()
    {
        if (_pendingDecision is not null || ActualHandGainObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame damage && damage.ParentFrameId == attack.Id ||
                    f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
