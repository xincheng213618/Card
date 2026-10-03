namespace CardGame.Core;

public sealed record ProgramOwnedDamagePointJudgment(int InstructionIndex, long DamageWindowId, long DamageOwnerFrameId,
    int OccurrenceIndex, long JudgmentFrameId, int DamageSourceSeat, int DamageTargetSeat, string Reason, string ResultBind);

public sealed partial class GameEngine
{
    private bool CanRunOwnedDamagePointJudgment(SkillProgramTrigger trigger, ProgramSkillWindowContext context,
        int ownerSeat)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartOwnedDamagePointJudgment)) return true;
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.OwnerSeat != ownerSeat ||
            ActiveDamageTrigger is not { TriggerWindow: SkillProgramTriggerWindow.AfterDamageApplied } damage ||
            context.ParentFrameId != damage.Id) return false;
        var attack = GetDamageTriggerAttack(damage);
        return !attack.IsSourceLess && context.SourceSeat == attack.SourceSeat && context.TargetSeat == attack.TargetSeat;
    }

    private SkillProgramStepOutcome BeginOwnedDamagePointJudgment(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var context = active.WindowContext;
        var index = _resolutionStack.FindIndex(f => f.Id == active.Id);
        if (index < 1 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame damage ||
            context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } || context.ParentFrameId != damage.Id ||
            damage.TriggerWindow != context.Window || damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            !MountObserverCandidateMatches(active, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
            _winner != Winner.None || !_players[active.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId) ||
            active.OwnedDamagePointJudgment is not null || effect.Target != SkillProgramEffectTarget.Owner ||
            effect.Visibility != SkillProgramCardSetVisibility.Public || effect.Op != SkillProgramEffectOp.StartOwnedDamagePointJudgment)
            throw new InvalidOperationException("An owned damage-point judgment requires its exact live source and current damage candidate.");
        var attack = GetDamageTriggerAttack(damage);
        if (attack.IsSourceLess || context.SourceSeat != attack.SourceSeat || context.TargetSeat != attack.TargetSeat)
            throw new InvalidOperationException("An owned damage-point judgment lost its real damage participants.");
        var receipt = new ProgramOwnedDamagePointJudgment(active.InstructionIndex, damage.Id, damage.ParentFrameId,
            context.OccurrenceIndex, _resolutionSequence + 1, attack.SourceSeat, attack.TargetSeat,
            effect.JudgmentReason!, effect.ResultBind!);
        ReplaceRuntimeTop(active with { OwnedDamagePointJudgment = receipt });
        // BeginJudgment allocates exactly the next frame before entering any child.
        // Keeping this scalar receipt first also covers a synchronous failed result.
        return StartProgramJudgment(GetActiveProgramFrame(active.Id), active.OwnerSeat, receipt.Reason, receipt.ResultBind,
            SkillProgramCardSetVisibility.Public, receipt.DamageSourceSeat);
    }

    private ProgramSkillFrame? OwnedDamagePointJudgmentRoot(long damageWindowId)
    {
        var damageIndex = _resolutionStack.FindIndex(f => f.Id == damageWindowId);
        if (damageIndex < 0 || damageIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[damageIndex] is not DamageTriggerWindowFrame damage ||
            damage.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
            damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            _resolutionStack[damageIndex + 1] is not ProgramSkillFrame root || root.OwnedDamagePointJudgment is not { } receipt ||
            receipt.DamageWindowId != damage.Id || receipt.DamageOwnerFrameId != damage.ParentFrameId ||
            receipt.OccurrenceIndex != damage.Candidates[damage.CandidateIndex].OccurrenceIndex ||
            receipt.DamageSourceSeat != damage.SourceSeat || receipt.DamageTargetSeat != damage.TargetSeat ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            context.ParentFrameId != damage.Id || context.SourceSeat != damage.SourceSeat || context.TargetSeat != damage.TargetSeat ||
            !MountObserverCandidateMatches(root, damage.Candidates[damage.CandidateIndex].ToProgramCandidate())) return null;
        var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
        if (receipt.InstructionIndex < 1 || receipt.InstructionIndex > plan.Instructions.Count ||
            root.InstructionIndex < receipt.InstructionIndex || root.InstructionIndex > receipt.InstructionIndex + 1 ||
            plan.Instructions[receipt.InstructionIndex - 1] is not { Op: SkillProgramEffectOp.StartOwnedDamagePointJudgment } start ||
            start.JudgmentReason != receipt.Reason || start.ResultBind != receipt.ResultBind) return null;
        for (var index = damageIndex + 2; index < _resolutionStack.Count; index++)
        {
            if (!OwnedDamagePointJudgmentEdge(index, root, receipt)) return null;
            // This exact Dying edge already belongs to the verified 3901
            // ancestry. The bound-Alcohol proof verifies its Program -> Use
            // producer and every remaining HP/card/movement observer edge,
            // including the frozen material and actual private-pile payment.
            // Do not then reject that same proven subtree as a native Peach.
            if (_resolutionStack[index] is DyingFrame dying &&
                IsPaidHandRepaymentProgramAlcoholRide(index, dying)) return root;
        }
        return root;
    }

    private bool OwnedDamagePointJudgmentEdge(int index, ProgramSkillFrame root, ProgramOwnedDamagePointJudgment receipt)
    {
        var child = _resolutionStack[index]; var parent = _resolutionStack[index - 1];
        if (child is JudgmentFrame judgment && parent.Id == root.Id)
            return judgment.Id == receipt.JudgmentFrameId && judgment.ParentFrameId == root.Id &&
                judgment.TargetSeat == root.OwnerSeat && judgment.SourceSeat == receipt.DamageSourceSeat &&
                judgment.Reason == receipt.Reason && judgment.Continuation == JudgmentContinuationKind.ProgramSkill &&
                judgment.ProgramResultBind == receipt.ResultBind && judgment.ProgramResultVisibility == SkillProgramCardSetVisibility.Public &&
                IsValidProgramJudgmentContinuation(judgment, root);
        if (child is ProgramJudgmentTriggerWindowFrame window && parent is JudgmentFrame judged)
            return judged.Id == receipt.JudgmentFrameId && window.ParentFrameId == judged.Id &&
                window.Judgment.JudgmentFrameId == judged.Id && window.Judgment.SubjectSeat == root.OwnerSeat &&
                window.Judgment.Reason == receipt.Reason && window.Judgment.SourceSeat == receipt.DamageSourceSeat;
        if (child is ProgramSkillFrame final && parent is ProgramJudgmentTriggerWindowFrame finalWindow)
        {
            if (finalWindow.Judgment.JudgmentFrameId != receipt.JudgmentFrameId ||
                finalWindow.CandidateIndex < 0 || finalWindow.CandidateIndex >= finalWindow.Candidates.Count ||
                final.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized, Judgment: { } finalized } context ||
                context.ParentFrameId != finalWindow.Id || finalized != finalWindow.Judgment) return false;
            var candidate = finalWindow.Candidates[finalWindow.CandidateIndex];
            return candidate.OwnerSeat == final.OwnerSeat && candidate.SkillId == final.SkillId && candidate.TriggerId == final.TriggerId &&
                candidate.SkillInstanceId == final.SkillInstanceId && candidate.GameplayHash == final.GameplayHash;
        }
        if (child is ProgramSkillFrame replacing && parent is JudgmentFrame replacementOwner)
            return replacementOwner.Id == receipt.JudgmentFrameId &&
                replacing.WindowContext is { Window: SkillProgramTriggerWindow.JudgmentReplacing, JudgmentReplacement: { } replacement } c &&
                c.ParentFrameId == replacementOwner.Id && replacement.JudgmentFrameId == replacementOwner.Id &&
                CurrentJudgmentCandidate(replacementOwner) is { } candidate && candidate.OwnerSeat == replacing.OwnerSeat &&
                candidate.ProgramId == replacing.SkillId && candidate.ProgramTriggerId == replacing.TriggerId &&
                candidate.SkillInstanceId == replacing.SkillInstanceId && candidate.GameplayHash == replacing.GameplayHash;
        if (parent.Id == root.Id && child is CardsMovedTriggerWindowFrame cleanup)
        {
            var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
            return root.InstructionIndex == receipt.InstructionIndex + 1 &&
                plan.Instructions[root.InstructionIndex - 1] is { Op: SkillProgramEffectOp.MoveBoundCards,
                    Destination: SkillProgramCardDestination.DiscardPile } effect && effect.SourceBind == receipt.ResultBind &&
                cleanup.Batch.ParentFrameId == root.Id && cleanup.Batch.OriginSkillId == root.SkillId &&
                cleanup.Batch.OriginSkillInstanceId == root.SkillInstanceId && cleanup.Batch.OriginOwnerSeat == root.OwnerSeat &&
                cleanup.Batch.Movements is [var movement] && _cardMovements.Contains(movement) &&
                GetProgramCardSet(root, receipt.ResultBind).CardIds.Contains(movement.CardId) &&
                movement.From == CardLocation.Processing && movement.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame;
        }
        if (RecoveryReplacementFrameRidesOn(child, parent) ||
            RandomEquipmentFrameRidesOn(child, parent) || PileEquipmentFrameRidesOn(child, parent)) return true;
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

    private bool HasOwnedDamagePointJudgmentObserver(long damageWindowId) =>
        OwnedDamagePointJudgmentRoot(damageWindowId) is not null;

    private bool HasOwnedDamagePointJudgmentRide(long judgmentFrameId) => ActiveDamageTrigger is { } damage &&
        OwnedDamagePointJudgmentRoot(damage.Id)?.OwnedDamagePointJudgment?.JudgmentFrameId == judgmentFrameId;

    private bool IsExactOwnedDamagePointJudgmentFinalizedChildSubtree(ProgramJudgmentTriggerWindowFrame window,
        JudgmentFrame? judgment, int windowIndex)
    {
        if (judgment is null || !window.Activated || window.ParentFrameId != judgment.Id ||
            windowIndex < 1 || _resolutionStack[windowIndex - 1] != judgment ||
            windowIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[windowIndex + 1] is not ProgramSkillFrame child ||
            child.WindowContext is not { Window: SkillProgramTriggerWindow.JudgmentFinalized } context ||
            context.ParentFrameId != window.Id || context.Judgment != window.Judgment ||
            ActiveDamageTrigger is not { } damage || OwnedDamagePointJudgmentRoot(damage.Id) is not { } root ||
            root.OwnedDamagePointJudgment is not { } receipt || judgment.ParentFrameId != root.Id ||
            receipt.JudgmentFrameId != judgment.Id || receipt.JudgmentFrameId != window.Judgment.JudgmentFrameId)
            return false;
        // The existing receipt proof walks every edge through this current
        // finalized candidate and its exact HP/movement/rescue descendants.
        // Preserve the frozen public result while only admitting this 3901 owner.
        return window.Judgment.SubjectSeat == root.OwnerSeat &&
            window.Judgment.SourceSeat == receipt.DamageSourceSeat && window.Judgment.Reason == receipt.Reason &&
            window.Judgment.CardId == judgment.CardId && window.Judgment.CardKind == judgment.CardKind &&
            window.Judgment.Suit == judgment.Suit && window.Judgment.Succeeded == judgment.Succeeded;
    }

    // A successfully claimed judgment is already disposed by a real rule child.
    // Only this explicit producer returns an empty cleanup binding for that case;
    // legacy StartJudgment keeps its original result-binding behavior.
    private bool IsOwnedDamagePointJudgmentResultClaimed(JudgmentFrame judgment)
    {
        if (ActiveDamageTrigger is not { } damage || OwnedDamagePointJudgmentRoot(damage.Id) is not { } root ||
            root.Id != judgment.ParentFrameId || root.OwnedDamagePointJudgment is not { } receipt ||
            receipt.JudgmentFrameId != judgment.Id || receipt.ResultBind != judgment.ProgramResultBind ||
            judgment.CardId is not { } cardId) return false;
        bool Claim(IGameEvent value) => value is ProgramJudgmentCardClaimedEvent claimed &&
            claimed.JudgmentFrameId == judgment.Id && claimed.OwnerSeat == judgment.TargetSeat && claimed.CardId == cardId;
        return _pendingEvents.Any(Claim) || _events.Any(e => Claim(e.Payload));
    }

    private bool IsOwnedDamagePointJudgmentProgramDying() => ActiveDamageTrigger is { } damage &&
        ActiveDying is { ResumesProgramSkill: true } dying && OwnedDamagePointJudgmentRoot(damage.Id) is not null &&
        _resolutionStack.OfType<DyingFrame>().Any(f => f.Id == dying.FrameId && f.ParentFrameId == dying.ParentFrameId);
}
