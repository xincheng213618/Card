namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Used only after a 6000-6004 root has proved its original issued receipt,
    // exact ledger and first child. The older generic observer edge is unchanged.
    private bool HalfHandPaidDamageObserverEdge(int index)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (PaidObserverDamageDyingFaceEdge(index)) return true;
        if (parent is ProgramSkillFrame attack && child is DyingFrame { Continuation: DyingContinuationKind.AttackHpLoss } replaced)
            return PaidObserverAttackHpLossDyingMatches(attack, replaced);
        if (parent is DyingFrame { Continuation: DyingContinuationKind.Damage or DyingContinuationKind.AttackHpLoss } dying && child is ProgramSkillFrame response)
            return PaidObserverDamageDyingProgramMatches(response, dying);
        return EquipmentDonationDamageObserverEdge(index);
    }

    private bool PaidObserverAttackHpLossDyingMatches(ProgramSkillFrame program, DyingFrame dying) =>
        program.AttackAttempt is { } attack && program.AttackReturn is not null &&
        dying.Continuation == DyingContinuationKind.AttackHpLoss && dying.ParentFrameId == program.Id &&
        dying.VictimSeat == attack.TargetSeat && dying.KillerSeat is null && ActiveDying?.FrameId == dying.Id &&
        CurrentDamageAttempt?.ResolutionId == program.Id &&
        CompleteProgramEventHistory().OfType<DamageReplacedWithHpLossEvent>().LastOrDefault(e => e.ParentResolutionId == program.Id) is { } applied &&
        applied.PolicyOwnerSeat == attack.SourceSeat && applied.TargetSeat == dying.VictimSeat && applied.Amount == attack.DamageAmount && applied.Amount > 0 && applied.RemainingHp <= 0 &&
        CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat is null) == 1;

    private bool PaidObserverDamageDyingProgramMatches(ProgramSkillFrame response, DyingFrame dying)
    {
        ProgramSkillFrame? producer;
        long? damageId;
        if (dying.Continuation == DyingContinuationKind.Damage &&
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == dying.ParentFrameId) is { } damage &&
            dying.VictimSeat == damage.TargetSeat)
        {
            producer = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == damage.ParentFrameId);
            damageId = damage.Id;
            if (producer?.AttackAttempt is not { } actual || dying.KillerSeat != (actual.SourceLess ? null : actual.SourceSeat)) return false;
        }
        else if (dying.Continuation == DyingContinuationKind.AttackHpLoss &&
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == dying.ParentFrameId) is { } replaced &&
            PaidObserverAttackHpLossDyingMatches(replaced, dying))
        { producer = replaced; damageId = null; }
        else return false;
        if (producer is not { AttackAttempt: { } attack, AttackReturn: not null } || ActiveDying?.FrameId != dying.Id ||
            dying.ResponderIndex < 0 || dying.ResponderIndex >= dying.ResponderSeats.Count ||
            CurrentDamageAttempt?.ResolutionId != producer.Id ||
            response.WindowContext is not { } context || context.ParentFrameId != dying.Id || context.DamageFrameId != damageId ||
            context.TargetSeat != dying.VictimSeat || context.SourceSeat != (attack.SourceLess ? null : attack.SourceSeat) ||
            context.OwnerSeat != response.OwnerSeat || response.OwnerSeat != dying.ResponderSeat ||
            context.Window is not (SkillProgramTriggerWindow.SelfDyingResponse or SkillProgramTriggerWindow.DyingResponse) ||
            context.Window == SkillProgramTriggerWindow.SelfDyingResponse && response.OwnerSeat != dying.VictimSeat ||
            string.IsNullOrWhiteSpace(response.TriggerId) || string.IsNullOrWhiteSpace(response.SkillInstanceId) ||
            response.ActivationId != response.TriggerId ||
            _contentRegistry.Skills.GetValueOrDefault(response.SkillId)?.Program is not { } definition || definition.GameplayHash != response.GameplayHash ||
            definition.Triggers.SingleOrDefault(t => t.Id == response.TriggerId)?.Window != context.Window ||
            context.OccurrenceIndex != 0) return false;
        // The real Dying producer uses the default occurrence zero. Its accepted
        // binding fact preserves the original instance after a paid child loses
        // that source; current trigger enumeration would revoke this return.
        return CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == response.Id &&
            e.SkillId == response.SkillId && e.BindingId == response.TriggerId && e.SkillInstanceId == response.SkillInstanceId &&
            e.OwnerSeat == response.OwnerSeat && e.Window == context.Window) == 1;
    }

    private bool PaidObserverDamageDyingFaceEdge(int index)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (!CharacterTurnedOverFrameRidesOn(child, parent)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame window && parent is ProgramSkillFrame response)
        {
            if (index < 2 || _resolutionStack[index - 2] is not DyingFrame dying ||
                !PaidObserverDamageDyingProgramMatches(response, dying) ||
                response.WindowContext?.Window != SkillProgramTriggerWindow.SelfDyingResponse ||
                window.ResumeProgramFrameId != response.Id || window.OwnerSeat != response.OwnerSeat || response.InstructionIndex < 1 ||
                ProgramInstructionResolver.Default.Resolve(response, _contentRegistry.GetSkill(response.SkillId).Program!)
                    .GetPausedInstruction(response.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.TurnOver } effect ||
                ResolveProgramEffectTarget(response, effect.Target) != window.OwnerSeat) return false;
            return CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == window.Id &&
                e.Change.ParentFrameId == response.Id && e.Change.TargetSeat == window.OwnerSeat &&
                e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver && e.Change.TurnedOver is { } face && face.WasFaceDown != face.IsFaceDown);
        }
        return child is ProgramSkillFrame observer && parent is ProgramLifecycleTriggerWindowFrame changed &&
            changed.CandidateIndex >= 0 && changed.CandidateIndex < changed.Candidates.Count &&
            MountObserverCandidateMatches(observer, changed.Candidates[changed.CandidateIndex]) &&
            observer.WindowContext is { Window: SkillProgramTriggerWindow.CharacterTurnedOver } context &&
            context.ParentFrameId == changed.Id && context.OwnerSeat == observer.OwnerSeat && context.TargetSeat == changed.OwnerSeat;
    }

    private bool HalfHandPaidObserverPrefix(int rootIndex, int lastIndex)
    {
        if (rootIndex < 0 || lastIndex <= rootIndex || lastIndex >= _resolutionStack.Count ||
            _resolutionStack[rootIndex] is not ProgramSkillFrame root || !HalfHandPhaseFirstChild(root, _resolutionStack[rootIndex + 1])) return false;
        for (var i = rootIndex + 1; i <= lastIndex; i++)
        {
            if (!HalfHandPaidDamageObserverEdge(i)) return false;
            if (_resolutionStack[i] is DyingFrame d && i < lastIndex &&
                (IsPaidHandRepaymentProgramAlcoholRide(i, d) || IsPaidHandRepaymentRescueRide(i, d) || PolicyCounterspellVirtualAlcoholRide(i, d) || PaidObserverDamageVirtualAlcoholRide(i, d)))
                return lastIndex == _resolutionStack.Count - 1;
        }
        return true;
    }

    // A new gain/HP observer may suspend the original Slash only when the
    // actual recipient's one-card support has already been delivered once.
    private bool AllowsHalfHandSupportNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 ||
            _resolutionStack.LastOrDefault()?.Id != observer.Id || observer.WindowContext?.Window is not
                (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.AfterHpRecovered or
                 SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost)) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.Damage || amount != effect.Amount || source != effect.ActorReference || nature != effect.DamageNature ||
            target != (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target))) return false;
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame { HalfHandSupport: { Paid: true } aid } root &&
                _resolutionStack[index - 1] is ActualUseTargetWindowFrame window && HalfHandSupportPaymentMatches(root, window, true) &&
                IsSlashCard(aid.Use.EffectiveKind) && ActiveCardAttack?.ResolutionId == aid.Use.CardUseFrameId &&
                CurrentDamageAttempt?.ResolutionId == aid.Use.CardUseFrameId &&
                HalfHandPaidObserverPrefix(index, _resolutionStack.Count - 1)) return true;
        return false;
    }

    private bool HasHalfHandPaidDamageObserver(long damageWindowId)
    {
        var windowIndex = _resolutionStack.FindIndex(f => f.Id == damageWindowId && f is DamageTriggerWindowFrame);
        if (windowIndex < 2 || _resolutionStack[windowIndex] is not DamageTriggerWindowFrame window ||
            _resolutionStack[windowIndex - 1] is not DamageFrame damage || damage.Id != window.ParentFrameId ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p => p.Id == damage.ParentFrameId) is not { AttackAttempt: not null, AttackReturn: not null } attack ||
            CurrentDamageAttempt?.ResolutionId != attack.Id) return false;
        var attackIndex = _resolutionStack.FindIndex(f => f.Id == attack.Id);
        for (var index = 0; index < attackIndex; index++)
            if (HalfHandPaidObserverPrefix(index, _resolutionStack.Count - 1)) return true;
        return false;
    }

    // The existing paid-counterspell producer only accepts ProgramSkill dying.
    // Damage dying retains the same exact zero-entity queued-recovery token.
    private bool PaidObserverDamageVirtualAlcoholRide(int dyingIndex, DyingFrame dying)
    {
        if (dyingIndex < 0 || dyingIndex + 3 >= _resolutionStack.Count ||
            _resolutionStack[dyingIndex + 1] is not ProgramSkillFrame program || !PaidObserverDamageDyingProgramMatches(program, dying) ||
            program.WindowContext?.Window != SkillProgramTriggerWindow.SelfDyingResponse || program.OwnerSeat != dying.VictimSeat || program.InstructionIndex < 1 ||
            ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!)
                .GetPausedInstruction(program.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.UseVirtualDyingAlcohol, Target: SkillProgramEffectTarget.Owner } ||
            _resolutionStack[dyingIndex + 2] is not CardUseFrame use || use.CardId != 0 || use.CardKind != CardKind.Alcohol ||
            use.SourceSeat != dying.VictimSeat || !use.TargetSeats.SequenceEqual([dying.VictimSeat]) || use.Action is not null || use.DyingResponse is not null ||
            use.VirtualBasicReturn is not null || use.PhysicalCardIds is not { Count: 0 } ||
            _resolutionStack[dyingIndex + 3] is not RecoveryReplacementFrame recovery || recovery.ParentFrameId != use.Id ||
            recovery.Return.ResumeFrameId != use.Id || recovery.Return.Continuation != PostEventContinuation.RecoveryProducer ||
            recovery.Attempt.SourceSeat != dying.VictimSeat || recovery.Attempt.TargetSeat != dying.VictimSeat || recovery.Attempt.Amount != 1 || recovery.Attempt.HpBefore > 0 ||
            recovery.Attempt.Completion is not { Producer: RecoveryAttemptProducer.DyingVirtualAlcohol, InstructionIndex: 0, CardId: 0, CardKind: CardKind.Alcohol } producer ||
            producer.ProgramFrameId != program.Id || producer.DyingFrameId != dying.Id || producer.SkillId != program.SkillId ||
            producer.SkillOwnerSeat != program.OwnerSeat || producer.MoveReason is not null ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id && e.CardId == 0 && e.CardKind == CardKind.Alcohol && e.SourceSeat == dying.VictimSeat) ||
            !CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Any(e => e.ResolutionId == use.Id && e.TargetSeats.SequenceEqual([dying.VictimSeat]))) return false;
        for (var i = dyingIndex + 4; i < _resolutionStack.Count; i++)
        {
            var child = _resolutionStack[i]; var parent = _resolutionStack[i - 1];
            if (child is ProgramSkillFrame observer && parent is HpChangedTriggerWindowFrame hp)
            {
                if (hp.CandidateIndex < 0 || hp.CandidateIndex >= hp.Candidates.Count || !MountObserverCandidateMatches(observer, hp.Candidates[hp.CandidateIndex]) ||
                    observer.WindowContext is not { HpChange: { } change } context || context.ParentFrameId != hp.Id || change != hp.Change || context.Window is not
                        (SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged)) return false;
                continue;
            }
            if (child is ProgramSkillFrame moveObserver && parent is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.CandidateIndex < 0 || moved.CandidateIndex >= moved.Candidates.Count || !MountObserverCandidateMatches(moveObserver, moved.Candidates[moved.CandidateIndex]) ||
                    moveObserver.WindowContext is not { MovementBatch: { } batch } moveContext || moveContext.ParentFrameId != moved.Id || batch.Id != moved.Batch.Id || moveContext.Window is not
                        (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived)) return false;
                continue;
            }
            if (!DamageFrameRidesOn(child, parent) && !RecoveryReplacementFrameRidesOn(child, parent) && !RandomEquipmentFrameRidesOn(child, parent) &&
                !PileEquipmentFrameRidesOn(child, parent)) return false;
        }
        return true;
    }
}
