namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long[] GameProgramActivationFrames(int owner, string skill, string activation, long beforeFrame = long.MaxValue) =>
        CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Where(e => e.OwnerSeat == owner && e.SkillId == skill &&
            e.ActivationId == activation && e.FrameId < beforeFrame).Select(e => e.FrameId).Distinct().Order().ToArray();
    private int GameProgramActivationCount(int ownerSeat, string skillId, string activationId) =>
        GameProgramActivationFrames(ownerSeat, skillId, activationId).Length;
    private bool CanOfferGameActivationAwakening(int ownerSeat, SkillProgramTrigger trigger) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AwakenAfterGameActivations) ||
        trigger.Effects is [{ Op: SkillProgramEffectOp.AwakenAfterGameActivations } effect] &&
        GameProgramActivationCount(ownerSeat, effect.SourceBind!, effect.StateId!) >= effect.MinimumValue;

    private bool ExactGameActivationAwakeningParent(ProgramSkillFrame f)
    {
        if (f.TriggerId is null || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } c ||
            c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat || f.OwnerSeat != _currentSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Continuation: ProgramLifecycleContinuation.NormalTurnStart } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1].Id == parent.Id;
    }

    private SkillProgramStepOutcome BeginGameActivationAwakening(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id); var trigger = GetProgramTrigger(f);
        if (f.GameActivationAwakening is not null || f.InstructionIndex != 1 || !ExactGameActivationAwakeningParent(f) ||
            trigger.Effects is not [var configured] || configured != effect || !CanOfferGameActivationAwakening(f.OwnerSeat, trigger) ||
            !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || _winner != Winner.None)
            throw new InvalidOperationException("Game-activation awakening lost its exact preparation candidate or actual activation threshold.");
        var owner = _players[f.OwnerSeat]; var before = owner.MaxHp; var after = Math.Max(1, before - 1);
        var r = new ProgramGameActivationAwakeningReceipt(1, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, f.WindowContext!.ParentFrameId, _turnNumber, _currentSeat, effect.SourceBind!, effect.StateId!,
            effect.MinimumValue, GameProgramActivationFrames(f.OwnerSeat, effect.SourceBind!, effect.StateId!, f.Id), effect.SkillIds,
            before, after, owner.Hp, Math.Min(owner.Hp, after), GameActivationAwakeningStage.MaximumPaid);
        ReplaceRuntimeTop(f = f with { GameActivationAwakening = r });
        // The existing primitive owns max-HP clamping and native MaximumHp health
        // observations. This family adds a typed continuation, not a Loss event.
        ChangeProgramMaximumHp(f, -1);
        AdvanceEventRulesAndQueueFact(new GameActivationAwakeningMaximumPaidEvent(f.Id, r.Source, r.GameplayHash, r.LifecycleFrameId,
            r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.CountedSkillId, r.CountedActivationId, r.MinimumValue,
            r.ActivationFrameIds.Count, r.MaximumBefore, r.MaximumAfter, r.HpBefore, r.HpAfter));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeGameActivationAwakening(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.GameActivationAwakening is not { } r) return false;
        AssertGameActivationAwakeningReceipt(f, ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!));
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        f = GetActiveProgramFrame(id); r = f.GameActivationAwakening!;
        if (r.Stage == GameActivationAwakeningStage.MaximumPaid && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        {
            ReplaceRuntimeTop(f = f with { GameActivationAwakening = r with { Stage = GameActivationAwakeningStage.GrantsIssued } });
            GrantProgramSkills(f, r.SkillIds);
            foreach (var skill in r.SkillIds) AdvanceEventRulesAndQueueFact(new GameActivationAwakeningGrantIssuedEvent(id, f.OwnerSeat, f.SkillId, skill));
            AdvanceRuntimeProgram(id); return true;
        }
        if (r.Stage == GameActivationAwakeningStage.Complete) { FinishProgramSkill(f, true); return true; }
        ReplaceRuntimeTop(f = f with { GameActivationAwakening = r with { Stage = GameActivationAwakeningStage.Complete } });
        AdvanceEventRulesAndQueueFact(new GameActivationAwakeningCompletedEvent(id, r.Stage == GameActivationAwakeningStage.GrantsIssued, _players[f.OwnerSeat].IsAlive));
        FinishProgramSkill(f, true); return true;
    }

    private bool ValidGameActivationAwakeningReceipt(ProgramSkillFrame f)
    {
        if (f.GameActivationAwakening is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 || !ExactGameActivationAwakeningParent(f) ||
            r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) || r.GameplayHash != f.GameplayHash ||
            r.LifecycleFrameId != f.WindowContext!.ParentFrameId || r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            r.ActualTurnOwnerSeat != f.OwnerSeat || !Enum.IsDefined(r.Stage) || r.MaximumBefore < 1 || r.MaximumAfter != Math.Max(1, r.MaximumBefore - 1) ||
            r.HpAfter != Math.Min(r.HpBefore, r.MaximumAfter) || r.MinimumValue < 1 || r.ActivationFrameIds.Count < r.MinimumValue ||
            !r.ActivationFrameIds.SequenceEqual(GameProgramActivationFrames(f.OwnerSeat, r.CountedSkillId, r.CountedActivationId, f.Id)) ||
            GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.AwakenAfterGameActivations } e] ||
            e.SourceBind != r.CountedSkillId || e.StateId != r.CountedActivationId || e.MinimumValue != r.MinimumValue || !e.SkillIds.SequenceEqual(r.SkillIds) ||
            r.SkillIds.Count == 0 || r.SkillIds.Distinct(StringComparer.Ordinal).Count() != r.SkillIds.Count || f.PendingMovementContinuation is not null) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<ProgramBindingStartedEvent>().Count(x => x.FrameId == f.Id && x.OwnerSeat == f.OwnerSeat && x.SkillId == f.SkillId &&
                x.BindingId == f.TriggerId && x.SkillInstanceId == f.SkillInstanceId && x.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) != 1 ||
            history.OfType<GameActivationAwakeningMaximumPaidEvent>().Count(x => x.FrameId == f.Id) != 1 ||
            history.OfType<GameActivationAwakeningMaximumPaidEvent>().Single(x => x.FrameId == f.Id) !=
                new GameActivationAwakeningMaximumPaidEvent(f.Id, r.Source, r.GameplayHash, r.LifecycleFrameId, r.ActualTurnNumber, r.ActualTurnOwnerSeat,
                    r.CountedSkillId, r.CountedActivationId, r.MinimumValue, r.ActivationFrameIds.Count, r.MaximumBefore, r.MaximumAfter, r.HpBefore, r.HpAfter) ||
            !history.OfType<MaximumHpChangedEvent>().Any(x => x.PlayerSeat == f.OwnerSeat && x.SkillId == f.SkillId &&
                x.Delta == r.MaximumAfter - r.MaximumBefore && x.MaximumHp == r.MaximumAfter)) return false;
        var grants = history.OfType<GameActivationAwakeningGrantIssuedEvent>().Where(x => x.FrameId == f.Id).ToArray();
        if (grants.Length != 0 && (grants.Any(x => x.OwnerSeat != f.OwnerSeat || x.SourceSkillId != f.SkillId) ||
                !grants.Select(x => x.GrantedSkillId).SequenceEqual(r.SkillIds)) ||
            r.Stage == GameActivationAwakeningStage.MaximumPaid && grants.Length != 0 ||
            r.Stage == GameActivationAwakeningStage.GrantsIssued && grants.Length != r.SkillIds.Count) return false;
        return true;
    }
    private void AssertGameActivationAwakeningReceipt(ProgramSkillFrame f, ProgramExecutionPlan plan)
    {
        if (f.GameActivationAwakening is null)
        {
            if (!plan.Instructions.Any(effect => effect.Op == SkillProgramEffectOp.AwakenAfterGameActivations))
                return;
            if (CompleteProgramEventHistory().OfType<GameActivationAwakeningMaximumPaidEvent>().Any(e => e.FrameId == f.Id) &&
                !CompleteProgramEventHistory().OfType<GameActivationAwakeningCompletedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("An issued game-activation awakening lost its owning receipt.");
            return;
        }
        if (!ValidGameActivationAwakeningReceipt(f)) throw new InvalidOperationException("Game-activation awakening lost its exact count, max-HP payment or grant continuation.");
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index + 1 < _resolutionStack.Count && !GameActivationAwakeningFirstChild(f, _resolutionStack[index + 1]))
            throw new InvalidOperationException("Game-activation awakening retained an unrelated first native child.");
    }
    private bool CanContinueGameActivationAwakening(ProgramSkillFrame f) => f.GameActivationAwakening is not null && ValidGameActivationAwakeningReceipt(f);
    private bool GameActivationAwakeningFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        var r = f.GameActivationAwakening!;
        if (r.Stage == GameActivationAwakeningStage.MaximumPaid && child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.Program && hp.Change.ParentFrameId == f.Id &&
                hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.SourceSeat is null && hp.Change.TargetSeat == f.OwnerSeat &&
                hp.Change.Amount == r.MaximumBefore - r.MaximumAfter && hp.Change.HpBefore == r.HpAfter && hp.Change.HpAfter == r.HpAfter;
        return r.Stage == GameActivationAwakeningStage.GrantsIssued && child is ProgramLifecycleTriggerWindowFrame skills &&
            skills.Window == SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId == f.Id &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && skills.CandidateIndex >= 0 && skills.CandidateIndex <= skills.Candidates.Count;
    }
    private bool IsGameActivationAwakeningProgramDying() => ActiveDying is { } dying && CategoryActivationObserverRoot(true) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private sealed partial class ProgramSkillHost : IGameActivationAwakeningProgramHost
    {
        public bool CanContinueGameActivationAwakening(ProgramSkillFrame f) => engine.CanContinueGameActivationAwakening(f);
        public SkillProgramStepOutcome BeginGameActivationAwakening(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginGameActivationAwakening(f, e);
    }
}
