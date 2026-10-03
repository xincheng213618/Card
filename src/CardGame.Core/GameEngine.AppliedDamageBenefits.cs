namespace CardGame.Core;

public sealed partial class GameEngine
{
    private string? DistinctTurnTargetUsage(SkillProgramActivation activation) => activation.Effects
        .SingleOrDefault(effect => effect.Op == SkillProgramEffectOp.ConsumeDistinctTurnTarget)?.StateId;

    private bool CanActivateDistinctTurnTarget(int ownerSeat, string skillId, string usageId, int targetSeat) =>
        _turnNumber > 0 && _currentSeat == ownerSeat && IsValidPlayerSeat(targetSeat) &&
        targetSeat != ownerSeat && _players[targetSeat].IsAlive &&
        !CompleteProgramEventHistory().OfType<ProgramDistinctTurnTargetCommittedEvent>().Any(e =>
            e.OwnerSeat == ownerSeat && e.SkillId == skillId && e.UsageId == usageId &&
            e.TargetSeat == targetSeat && e.TurnNumber == _turnNumber && e.TurnSeat == _currentSeat);

    private void ConsumeDistinctProgramTurnTarget(ProgramSkillFrame frame, string usageId)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var activation = ProgramInstructionResolver.Default.Resolve(active, AppliedDamageProgramDefinition(active.SkillId)).Activation;
        if (active.TriggerId is not null || active.SelectedTargetSeats is not [var target] ||
            activation is null || DistinctTurnTargetUsage(activation) != usageId || active.InstructionIndex != 1 ||
            activation.Effects[0].Op != SkillProgramEffectOp.ConsumeDistinctTurnTarget ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId) ||
            !CanActivateDistinctTurnTarget(active.OwnerSeat, active.SkillId, usageId, target))
            throw new InvalidOperationException("An actual-turn target commitment lost its exact active source, first instruction or unvisited target.");
        AdvanceEventRulesAndQueueFact(new ProgramDistinctTurnTargetCommittedEvent(active.Id, active.OwnerSeat,
            active.SkillId, active.ActivationId, active.SkillInstanceId, active.GameplayHash, usageId, target,
            _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId));
    }

    private SkillProgramStepOutcome ReceiveProgramOwnerDamage(ProgramSkillFrame frame, int amount)
    {
        if (frame.TriggerId is not null || frame.OwnerSeat != _currentSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("Receiving damage as an active cost requires the actual owner's Play phase.");
        // The official text says receive damage; it does not designate a source.
        // This goes through the real damage attempt, prevention, HP and dying flow.
        return BeginProgramSkillDamage(frame, frame.OwnerSeat, amount, sourceLess: true);
    }

    private SkillProgramStepOutcome BeginAppliedDamageBenefitDraw(ProgramSkillFrame frame, int amount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var index = _resolutionStack.FindIndex(f => f.Id == active.Id);
        if (active.AppliedDamageBenefit is not null || index < 1 ||
            _resolutionStack[index - 1] is not DamageTriggerWindowFrame damage ||
            active.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            context.ParentFrameId != damage.Id || context.DamageFrameId != damage.ParentFrameId ||
            context.TargetSeat != damage.TargetSeat || damage.TriggerWindow != context.Window ||
            damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            !MountObserverCandidateMatches(active, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()))
            throw new InvalidOperationException("An applied-damage Draw requires its exact actual producer and frozen current candidate.");
        var attack = GetDamageTriggerAttack(damage);
        if (!attack.DamageWasApplied || attack.DamageAmount <= 0 || context.Amount != attack.DamageAmount)
            throw new InvalidOperationException("An applied-damage benefit requires positive actual applied damage.");
        if (_winner != Winner.None || !_players[active.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var first = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        ReplaceRuntimeTop(active with { AppliedDamageBenefit = new(active.InstructionIndex, damage.Id,
            damage.ParentFrameId, active.OwnerSeat, damage.TargetSeat, context.SourceSeat, amount, first, first, 0),
            PendingMovementContinuation = new(active.OwnerSeat, 0, null) });
        var drawn = DrawCards(_players[active.OwnerSeat], amount, true, new("program.applied-damage-benefit.draw"));
        active = GetActiveProgramFrame(active.Id);
        ReplaceRuntimeTop(active with { AppliedDamageBenefit = active.AppliedDamageBenefit! with
        { LastMovementSequence = _cardMovements.LastOrDefault()?.Sequence ?? first, DrawCount = drawn.Count } });
        if (!TryBeginQueuedRecoveryReplacement(active.Id, PostEventContinuation.AwaitedProgramMovement) &&
            !TryBeginHpChangedProgramWindow(active.Id, PostEventContinuation.AwaitedProgramMovement) &&
            !TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgram AppliedDamageProgramDefinition(string skillId) => _contentRegistry.GetSkill(skillId).Program!;

    private sealed partial class ProgramSkillHost : IAppliedDamageBenefitProgramHost
    {
        public SkillProgramStepOutcome ReceiveOwnerDamage(ProgramSkillFrame frame, int amount) => engine.ReceiveProgramOwnerDamage(frame, amount);
        public void ConsumeDistinctTurnTarget(ProgramSkillFrame frame, string usageId) => engine.ConsumeDistinctProgramTurnTarget(frame, usageId);
        public SkillProgramStepOutcome DrawOwnerAtAppliedDamage(ProgramSkillFrame frame, int amount) => engine.BeginAppliedDamageBenefitDraw(frame, amount);
    }
}
