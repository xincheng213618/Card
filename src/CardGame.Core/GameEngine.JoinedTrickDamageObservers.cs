namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool JoinedTrickDamageRewardFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (frame.JoinedTrickDamageRewardReceipt is not { } receipt || !ValidJoinedTrickDamageReward(frame)) return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.ResumeProgramFrameId is null && moved.Batch.Id == moved.Id && moved.Batch.ParentFrameId == frame.Id &&
                moved.Batch.AwaitingProgramFrameId == frame.Id && moved.Batch.OriginOwnerSeat == frame.OwnerSeat &&
                moved.Batch.OriginSkillId == frame.SkillId && moved.Batch.OriginSkillInstanceId == frame.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    m.Sequence > receipt.MovementSequenceBefore && m.Sequence <= receipt.MovementSequenceAfter &&
                    m.Reason.Value == JoinedTrickRewardReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat));
        if (child is ProgramLifecycleTriggerWindowFrame skills && skills.Window == SkillProgramTriggerWindow.SkillsChanged)
            return skills.ResumeProgramFrameId == frame.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram &&
                skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
        return child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            state.ResumeProgramFrameId == frame.Id && state.CharacterStateContinuation == CharacterStateContinuation.Program &&
            state.CandidateIndex >= 0 && state.CandidateIndex <= state.Candidates.Count &&
            CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == frame.Id &&
                e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
    }

    private bool JoinedTrickDamageRewardStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        parent is ProgramSkillFrame root && JoinedTrickDamageRewardFirstChild(root, child) ||
        parent is ProgramCardTriggerWindowFrame window && child is ProgramSkillFrame { JoinedTrickDamageRewardReceipt: not null } reward &&
            reward.WindowContext?.ParentFrameId == window.Id && ValidJoinedTrickDamageReward(reward);

    private ProgramSkillFrame? JoinedTrickDamageRewardObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame root && JoinedTrickDamageRewardFirstChild(root, _resolutionStack[index + 1]) &&
                SameNameHandObserverSuffix(index)) return root;
        return null;
    }

    private bool IsJoinedTrickDamageRewardDying() => ActiveDying is { } dying && JoinedTrickDamageRewardObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasJoinedTrickDamageRewardCardObserver(long useId) => JoinedTrickDamageRewardObserverRoot() is { } root &&
        root.JoinedTrickDamageRewardReceipt!.Benefit.CardUseFrameId == useId;
    private bool HasJoinedTrickDamageRewardDamageObserver(long id) => HasJoinedTrickDamageRewardObserver(id, false);
    private bool HasJoinedTrickDamageRewardBeforeDamageObserver(long id) => HasJoinedTrickDamageRewardObserver(id, true);
    private bool HasJoinedTrickDamageRewardObserver(long id, bool before)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == id && (before ? f is BeforeDamageProgramWindowFrame : f is DamageTriggerWindowFrame));
        if (index < 0 || JoinedTrickDamageRewardObserverRoot() is not { } root) return false;
        var rootIndex = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index > rootIndex) return true;
        for (var child = index + 1; child <= rootIndex; child++)
            if (!JoinedTrickDamageRewardStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !ResponseCompletionStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child]) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[child - 1], _resolutionStack[child])) return false;
        return index < rootIndex;
    }

    private bool AllowsJoinedTrickDamageRewardNestedDamage(ProgramSkillFrame observer, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != observer.Id ||
            observer.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or
                SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged) ||
            JoinedTrickDamageRewardObserverRoot() is not { } root || root.Id == observer.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(observer, _contentRegistry.GetSkill(observer.SkillId).Program!)
            .GetPausedInstruction(observer.InstructionIndex).Effect;
        return effect.Op == SkillProgramEffectOp.Damage && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(observer, reference) : ResolveProgramEffectTarget(observer, effect.Target));
    }

    private bool TryAdvanceJoinedTrickDamageRewardSubtree()
    {
        if (_pendingDecision is not null || JoinedTrickDamageRewardObserverRoot() is null) return false;
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
