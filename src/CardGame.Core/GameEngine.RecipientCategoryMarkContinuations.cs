namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactRecipientCategoryDyingCursor(ProgramSkillFrame root, DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry)
    {
        if (root.RecipientCategoryMark?.OriginalDyingCursor is not { } old || dying.Id != old.DyingFrameId || dying.ParentFrameId != old.ParentFrameId ||
            dying.VictimSeat != root.OwnerSeat || dying.VictimSeat != old.VictimSeat || dying.KillerSeat != old.KillerSeat || dying.Continuation != old.Continuation ||
            dying.Step != old.Step || dying.ResponderIndex != old.ResponderIndex || !dying.ResponderSeats.SequenceEqual(old.ResponderSeats) ||
            !dying.AttemptedSelfDyingBindings.SequenceEqual(old.AttemptedSelfDyingBindings) || dying.PendingRecoveryAttempts is { Count: > 0 } || dying.PaidFactionRequestCostRecovery is not null ||
            entry.Id != old.EntryFrameId || entry.CandidateIndex != old.CandidateIndex || entry.Step != old.EntryStep || entry.Candidates[entry.CandidateIndex] != old.Candidate ||
            _resolutionStack.SingleOrDefault(f => f.Id == old.ParentFrameId) is not { } parent || parent.Kind != old.ParentKind || parent.Step != old.ParentStep ||
            _resolutionStack.FindIndex(f => f.Id == parent.Id) + 1 != _resolutionStack.FindIndex(f => f.Id == dying.Id) ||
            DyingSuitsCursorHash<ResolutionFrame>(parent) != old.ParentHash ||
            CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) != 1 ||
            CompleteProgramEventHistory().OfType<RecipientCategoryMarkDyingCursorIssuedEvent>().Count(e => e.FrameId == root.Id && e.DyingFrameId == dying.Id && e.CursorHash == DyingSuitsCursorHash(old)) != 1) return false;
        if (dying.Continuation == DyingContinuationKind.Damage)
            return parent is DamageFrame damage && damage.TargetSeat == dying.VictimSeat && damage.ParentFrameId == old.AttackOwnerFrameId &&
                _resolutionStack.SingleOrDefault(f => f.Id == old.AttackOwnerFrameId) is { } owner &&
                _resolutionStack.FindIndex(f => f.Id == owner.Id) + 1 == _resolutionStack.FindIndex(f => f.Id == damage.Id) && DyingSuitsCursorHash<ResolutionFrame>(owner) == old.AttackOwnerHash;
        return old.AttackOwnerFrameId is null && old.AttackOwnerHash is null &&
            (dying.Continuation == DyingContinuationKind.ProgramSkill && parent is ProgramSkillFrame ||
             dying.Continuation == DyingContinuationKind.AttackHpLoss && parent is ProgramSkillFrame or CardUseFrame or JudgmentFrame);
    }
    private bool RecipientCategoryObserverSuffix(int rootIndex)
    {
        for (var i = rootIndex + 2; i < _resolutionStack.Count; i++)
        {
            var parent = _resolutionStack[i - 1]; var child = _resolutionStack[i];
            if (child is ProgramLifecycleTriggerWindowFrame changed && parent is ProgramSkillFrame p && changed.Window == SkillProgramTriggerWindow.SkillsChanged &&
                changed.ResumeProgramFrameId == p.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && changed.CandidateIndex >= 0 && changed.CandidateIndex <= changed.Candidates.Count) continue;
            if (!RecipientCategoryMarkStructuralEdge(parent, child) && !OriginalHandEntityStructuralEdge(parent, child) && !OutsidePhaseDrawDiscardStructuralEdge(parent, child) &&
                !RoundGainedEquipmentDrawStructuralEdge(parent, child) && !JoinedTrickDamageRewardStructuralEdge(parent, child) && !PairedColorDispositionStructuralEdge(parent, child) && !SameNameHandStructuralEdge(parent, child) && !CompletedUndamagedTargetRevealStructuralEdge(parent, child) && !ResponseCompletionStructuralEdge(parent, child) &&
                !CardSupplyCompletionStructuralEdge(parent, child) && !DyingSuitsStructuralEdge(parent, child) && !HalfHandPaidDamageObserverEdge(i) && !PaidTargetObserverEdge(i)) return false;
        }
        return true;
    }
    private bool RecipientCategoryStructuralPrefix(ProgramSkillFrame f)
    {
        if (!ValidRecipientCategoryMarkReceipt(f)) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return i >= 1 && (i == _resolutionStack.Count - 1 || RecipientCategoryMarkFirstChild(f, _resolutionStack[i + 1]) && RecipientCategoryObserverSuffix(i));
    }
    private bool RecipientCategoryPureDyingPrefix(ProgramSkillFrame f)
    {
        if (!ValidRecipientCategoryMarkReceipt(f)) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 2) return false;
        if (i == _resolutionStack.Count - 1) return true;
        if (!RecipientCategoryMarkFirstChild(f, _resolutionStack[i + 1])) return false;
        // DyingSuitsStructuralEdge is a native-edge predicate which never queries
        // ActiveDying. Do not call observer-root predicates from this filter.
        for (var child = i + 2; child < _resolutionStack.Count; child++)
        {
            var parent = _resolutionStack[child - 1]; var current = _resolutionStack[child];
            if (current is ProgramLifecycleTriggerWindowFrame skills && parent is ProgramSkillFrame acquired && skills.Window == SkillProgramTriggerWindow.SkillsChanged &&
                skills.ResumeProgramFrameId == acquired.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count) continue;
            if (!DyingSuitsStructuralEdge(parent, current) && !OrderedPrintedSkillLossStructuralEdge(parent, current) &&
                !OverflowTargetCancellationStructuralEdge(parent, current) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(parent, current)) return false;
        }
        return true;
    }
    // This ActiveDying filter is pure: it does not call ActiveDying or observer
    // predicates that depend on it. It hides only the frozen original entry.
    private bool IsOriginalDyingSuspendedByRecipientCategoryMark(DyingFrame dying) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.RecipientCategoryMark is { Consumed: true, RecoveryIssued: true, Stage: not RecipientCategoryMarkStage.Complete } r &&
        r.DyingFrameId == dying.Id && RecipientCategoryPureDyingPrefix(f));
    private ProgramSkillFrame? RecipientCategoryMarkObserverRoot()
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
            if (_resolutionStack[i] is ProgramSkillFrame f && RecipientCategoryMarkFirstChild(f, _resolutionStack[i + 1]) && RecipientCategoryObserverSuffix(i)) return f;
        return null;
    }
    private bool IsRecipientCategoryMarkProgramDying() => ActiveDying is { } dying && RecipientCategoryMarkObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasRecipientCategoryMarkDamageObserver(long windowId)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == windowId && f is DamageTriggerWindowFrame);
        if (index < 0 || RecipientCategoryMarkObserverRoot() is not { } root) return false;
        var rootIndex = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index > rootIndex) return true; // The validated suffix contains this exact native descendant.
        // An enclosing damage observer is accepted only if each edge from its
        // actual candidate through the original Dying entry to this root agrees.
        for (var child = index + 1; child <= rootIndex; child++)
            if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child])) return false;
        return index < rootIndex;
    }
    private bool AllowsRecipientCategoryMarkNestedDamage(ProgramSkillFrame observer, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.DiscardPileReceived or
                SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            RecipientCategoryMarkObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var e = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return e.Op == SkillProgramEffectOp.Damage && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, e.Target));
    }
    private bool TryAdvanceRecipientCategoryMarkSubtree()
    {
        if (_pendingDecision is not null || RecipientCategoryMarkObserverRoot() is null) return false;
        var top = _resolutionStack.LastOrDefault();
        if (top is ProgramSkillFrame { AttackAttempt: not null } attack)
        {
            if (CurrentDamageAttempt?.ResolutionId != attack.Id || ActiveDying is not null || attack.AttackReturn is null ||
                _resolutionStack.Any(f => f is DamageFrame d && d.ParentFrameId == attack.Id || f is BeforeDamageProgramWindowFrame b && (b.ContinuationAttackResolutionId ?? b.ParentFrameId) == attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this, attack.Id)); AdvanceRulesAndPublishState(); return true;
        }
        if (top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or ProgramCardTriggerWindowFrame or
            BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame or RecoveryReplacementFrame)
        { AdvanceRuntimeFrame(top.Id); AdvanceRulesAndPublishState(); return true; }
        if (top is DeathFrame death) { ContinueDeathResolution(death.Id); AdvanceRulesAndPublishState(); return true; }
        return false;
    }
}
