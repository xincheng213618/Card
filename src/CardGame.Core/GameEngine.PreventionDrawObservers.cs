namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsValidPreventionDrawReceipt(ProgramSkillFrame root, BeforeDamageProgramWindowFrame parent)
    {
        if (root.PreventionDrawReceipt is not { } receipt || root.WindowContext is not
            { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            context.ParentFrameId != parent.Id || context.TargetSeat != root.OwnerSeat || context.Amount != parent.Amount ||
            receipt.BeforeDamageFrameId != parent.Id || receipt.TargetSeat != root.OwnerSeat || parent.TargetSeat != root.OwnerSeat ||
            receipt.SourceSeat != parent.SourceSeat || receipt.PreventedAmount != parent.Amount || !parent.Prevented ||
            parent.Amount <= 0 || receipt.InstructionIndex != root.InstructionIndex - 1 ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(root, parent.Candidates[parent.CandidateIndex].Candidate)) return false;
        var instructions = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!).Instructions;
        if (receipt.InstructionIndex < 0 || receipt.InstructionIndex >= instructions.Count ||
            instructions[receipt.InstructionIndex] is not { Op: SkillProgramEffectOp.PreventCurrentDamageAndDrawMultiple } effect ||
            effect.Amount != receipt.Multiplier || receipt.ActualDrawCount < 0 || receipt.ActualDrawCount > checked(parent.Amount * effect.Amount) ||
            receipt.MovementSequenceBefore < 0 || receipt.MovementSequenceAfter < receipt.MovementSequenceBefore) return false;
        var movements = _cardMovements.Where(m => m.Sequence > receipt.MovementSequenceBefore && m.Sequence <= receipt.MovementSequenceAfter).ToArray();
        bool IsActualDraw(CardMovementRecord m) => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(root.OwnerSeat) &&
            m.Reason.Value == $"skill-program.{root.SkillId}.prevented-damage-draw";
        if (movements.Count(IsActualDraw) != receipt.ActualDrawCount || movements.Any(m => !IsActualDraw(m) &&
            !(m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle))) return false;
        return _events.Select(item => item.Payload).Concat(_pendingEvents).OfType<ProgramDamagePreventedEvent>().Any(fact =>
            fact.FrameId == parent.Id && fact.SkillId == root.SkillId && fact.BindingId == root.TriggerId &&
            fact.OwnerSeat == root.OwnerSeat && fact.SourceSeat == parent.SourceSeat && fact.TargetSeat == parent.TargetSeat && fact.Amount == parent.Amount);
    }

    private ProgramSkillFrame? PreventionDrawObserverRoot(long beforeDamageId)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index - 1] is not BeforeDamageProgramWindowFrame parent || parent.Id != beforeDamageId ||
                _resolutionStack[index] is not ProgramSkillFrame root || !IsValidPreventionDrawReceipt(root, parent) ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat ||
                _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame first ||
                first.Batch.ParentFrameId != root.Id || first.Batch.AwaitingProgramFrameId is { } waiting && waiting != root.Id ||
                first.Batch.OriginSkillId != root.SkillId || first.Batch.OriginSkillInstanceId != root.SkillInstanceId || first.Batch.OriginOwnerSeat != root.OwnerSeat ||
                first.Batch.Movements.Count == 0 || first.Batch.Movements.Any(m => !_cardMovements.Contains(m) ||
                    m.Sequence <= root.PreventionDrawReceipt!.MovementSequenceBefore || m.Sequence > root.PreventionDrawReceipt.MovementSequenceAfter)) continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                // This helper proves the entire exact program/Alcohol use and
                // its descendants. Do not re-check those already proved edges
                // as if the Alcohol use were a direct native Dying response.
                if (_resolutionStack[child - 1] is DyingFrame alcoholDying &&
                    IsPaidHandRepaymentProgramAlcoholRide(child - 1, alcoholDying)) break;
                if (!PreventionDrawObserverEdge(child)) { aligned = false; break; }
            }
            if (aligned) return root;
        }
        return null;
    }

    private bool PreventionDrawObserverEdge(int index)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (ProvenanceClaimTurnedOverEdge(child, parent)) return true;
        if (RecoveryReplacementFrameRidesOn(child, parent) || RandomEquipmentFrameRidesOn(child, parent) ||
            PileEquipmentFrameRidesOn(child, parent)) return true;
        if (parent is DeathFrame death)
        {
            if (child is ProgramDeathTriggerWindowFrame ownerDeath)
                return ownerDeath.DeathFrameId == death.Id && ownerDeath.OwnerSeat == death.VictimSeat && ownerDeath.KillerSeat == death.KillerSeat;
            if (child is ProgramKillTriggerWindowFrame characterDeath)
                return characterDeath.DeathFrameId == death.Id && characterDeath.VictimSeat == death.VictimSeat &&
                    characterDeath.KillerSeat == death.KillerSeat && characterDeath.Candidates.Count == characterDeath.Contexts.Count &&
                    characterDeath.Contexts.All(c => c.Window == SkillProgramTriggerWindow.CharacterDied && c.ParentFrameId == characterDeath.Id &&
                        c.TargetSeat == death.VictimSeat && c.SourceSeat == death.KillerSeat);
        }
        if (child is ProgramSkillFrame deathObserver && parent is ProgramDeathTriggerWindowFrame deadWindow)
            return deadWindow.CandidateIndex >= 0 && deadWindow.CandidateIndex < deadWindow.Candidates.Count &&
                MountObserverCandidateMatches(deathObserver, deadWindow.Candidates[deadWindow.CandidateIndex]) &&
                deathObserver.WindowContext is { Window: SkillProgramTriggerWindow.OwnerDied } c && c.ParentFrameId == deadWindow.Id &&
                c.OwnerSeat == deadWindow.OwnerSeat && c.TargetSeat == deadWindow.OwnerSeat && c.SourceSeat == deadWindow.KillerSeat;
        if (child is ProgramSkillFrame killObserver && parent is ProgramKillTriggerWindowFrame killWindow)
            return killWindow.CandidateIndex >= 0 && killWindow.CandidateIndex < killWindow.Candidates.Count &&
                killWindow.Candidates.Count == killWindow.Contexts.Count && MountObserverCandidateMatches(killObserver, killWindow.Candidates[killWindow.CandidateIndex]) &&
                killObserver.WindowContext is { Window: SkillProgramTriggerWindow.CharacterDied } c &&
                c.ParentFrameId == killWindow.Id && c.OwnerSeat == killObserver.OwnerSeat && c.TargetSeat == killWindow.VictimSeat &&
                c.SourceSeat == killWindow.KillerSeat && c.OccurrenceIndex == killWindow.Contexts[killWindow.CandidateIndex].OccurrenceIndex;
        if (parent is DyingFrame dying)
        {
            if (child is CardUseFrame) return IsPaidHandRepaymentRescueRide(index - 1, dying);
            if (child is ProgramSkillFrame && IsPaidHandRepaymentProgramAlcoholRide(index - 1, dying)) return true;
            if (ParticipantHandDyingEntryRide(child, parent, dying)) return true;
        }
        if (child is ProgramSkillFrame entryProgram && parent is ProgramLifecycleTriggerWindowFrame entry &&
            _resolutionStack.OfType<DyingFrame>().LastOrDefault(f => f.Id == entry.ResumeDyingFrameId) is { } entryDying &&
            ParticipantHandDyingEntryRide(entryProgram, entry, entryDying)) return true;
        if (child is HpChangedTriggerWindowFrame hp && parent is CardUseFrame rescue && index >= 2 &&
            _resolutionStack[index - 2] is DyingFrame rescued && IsPaidHandRepaymentRescueRide(index - 2, rescued))
            return ParticipantHandRescueObserverRide(hp, rescue, rescue);
        if (_resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.DyingResponse is not null) is { } rescueUse &&
            ParticipantHandRescueObserverRide(child, parent, rescueUse)) return true;
        return PaidMountObserverEdge(index);
    }

    private bool HasPreventionDrawDying(long beforeDamageId) => ActiveDying is { ResumesProgramSkill: true } dying &&
        PreventionDrawObserverRoot(beforeDamageId) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);

    private bool IsPreventionDrawProgramDying() => _resolutionStack.OfType<BeforeDamageProgramWindowFrame>()
        .Any(parent => HasPreventionDrawDying(parent.Id));

    // An outer after-damage cursor is admitted only when this exact original
    // program attack return names it and the ancestry still contains that live
    // candidate. A different prevention/Dying chain cannot bless the cursor.
    private bool IsPreventionDrawInsideDamageProgramDying()
    {
        if (ActiveDamageTrigger is not { } damage) return false;
        foreach (var before in _resolutionStack.OfType<BeforeDamageProgramWindowFrame>())
        {
            if (!HasPreventionDrawDying(before.Id)) continue;
            var attackId = before.ContinuationAttackResolutionId ?? before.ParentFrameId;
            var index = _resolutionStack.FindIndex(f => f.Id == attackId);
            if (index < 1 || _resolutionStack[index] is not ProgramSkillFrame { AttackAttempt: not null, AttackReturn: { } returned } attack ||
                returned.ParentDamageWindowFrameId != damage.Id || _resolutionStack[index - 1] is not DamageTriggerWindowFrame parent || parent.Id != damage.Id ||
                parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
                !MountObserverCandidateMatches(attack, parent.Candidates[parent.CandidateIndex].ToProgramCandidate()) ||
                attack.WindowContext?.ParentFrameId != parent.Id) continue;
            return true;
        }
        return false;
    }
}
