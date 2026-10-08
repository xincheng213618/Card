namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool PhaseNamePredictionFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.PhaseNamePrediction is not { Stage: PhaseNamePredictionStage.CostChildren or PhaseNamePredictionStage.ClaimChildren } r ||
            root.PendingMovementContinuation is null || r.SequenceAfter <= r.SequenceBefore) return false;
        AssertPhaseNamePrediction(root);
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == root.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return state.ResumeProgramFrameId == root.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
                state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id &&
                    e.Change.ParentFrameId == root.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        var reason = r.Stage == PhaseNamePredictionStage.CostChildren ? PhasePredictionCostReason : PhasePredictionClaimReason;
        bool MovedEquipment(PhaseNamePredictionMaterial material, CardKind kind) =>
            r.Selected.Contains(material.CardId) && material.From.Zone == CardZoneKind.Equipment && GetAttackCard(material.CardId).Kind == kind &&
            _cardMovements.Any(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter &&
                m.CardId == material.CardId && m.From == material.From && m.Reason.Value == reason);
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter ||
                     m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.To == CardLocation.DiscardPile &&
                     r.Eligible.Any(material => MovedEquipment(material, CardKind.WoodenOx) &&
                         m.From == CardLocation.WoodenOxGrain(material.From.OwnerSeat!.Value))));
        var lionOwner = r.Eligible.Where(material => MovedEquipment(material, CardKind.SilverLion)).Select(material => material.From.OwnerSeat).FirstOrDefault();
        if (lionOwner is not { } armorOwner) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == armorOwner && hp.Change.TargetSeat == armorOwner && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery, root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.SourceSeat == armorOwner && recovery.Attempt.TargetSeat == armorOwner && recovery.Attempt.Amount == 1 &&
            recovery.Attempt.Completion.MoveReason?.Value == reason;
    }
    private ProgramSkillFrame? PhaseNamePredictionObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
        {
            if (_resolutionStack[i] is not ProgramSkillFrame root || !PhaseNamePredictionFirstChild(root, _resolutionStack[i + 1])) continue;
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
    private bool AllowsPhaseNamePredictionNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.SkillsChanged) ||
            PhaseNamePredictionObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool IsPhaseNamePredictionDying() => ActiveDying is { } dying && PhaseNamePredictionObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasPhaseNamePredictionDamageObserver(long window) => _resolutionStack.Any(f => f.Id == window && f is DamageTriggerWindowFrame) && PhaseNamePredictionObserverRoot() is not null;
    private bool TryAdvancePhaseNamePredictionSubtree()
    {
        if (_pendingDecision is not null || PhaseNamePredictionObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame b &&
                    (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or
            ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
