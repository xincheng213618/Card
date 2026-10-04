namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FireTargetDrawReason = "program.fire-target-benefit.draw";
    private bool CanRunFireTargetBenefit(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota)) return true;
        if (context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.SourceSeat != candidate.OwnerSeat ||
            context.TargetSeat is not { } target || !IsValidPlayerSeat(target) || !_players[target].IsAlive ||
            ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId || damage.ParentFrameId != context.DamageFrameId)
            return false;
        var attack = GetDamageTriggerAttack(damage);
        return attack.DamageWasApplied && attack.DamageAmount > 0 && !attack.IsSourceLess &&
            attack.SourceSeat == candidate.OwnerSeat && attack.TargetSeat == target && GetDamageNature(attack) == DamageNature.Fire;
    }
    private SkillProgramStepOutcome BeginFireTargetBenefit(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.FireTargetBenefit is not null || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } c ||
            c.TargetSeat is not { } target || ActiveDamageTrigger is not { } damage || damage.Id != c.ParentFrameId ||
            damage.ParentFrameId != c.DamageFrameId || damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
            !MountObserverCandidateMatches(f, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
            !CanRunFireTargetBenefit(damage.Candidates[damage.CandidateIndex].ToProgramCandidate(), GetProgramTrigger(f), c))
            throw new InvalidOperationException("The fire-target benefit lost its exact actual applied-damage candidate.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        ReplaceRuntimeTop(f = f with { FireTargetBenefit = new(f.InstructionIndex, damage.Id, damage.ParentFrameId,
            f.OwnerSeat, target, _turnNumber, _currentSeat, before, before, 0, 0, CreateProgramTurnEffectSource(f)),
            PendingMovementContinuation = new(target, 0, null) });
        // Freeze only the promise; the quota does not exist during draw children.
        var drawn = DrawCards(_players[target], 1, true, new(FireTargetDrawReason));
        f = GetActiveProgramFrame(f.Id);
        var after = _cardMovements.LastOrDefault()?.Sequence ?? before;
        ReplaceRuntimeTop(f = f with { FireTargetBenefit = f.FireTargetBenefit! with
            { SequenceAfter = after, DrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new ProgramFireTargetDrawIssuedEvent(f.Id, damage.Id, f.OwnerSeat, target, drawn.Count, before, after));
        if (!TryDrainFireTargetMovement(f)) ReturnRuntimeProgramMovement(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool TryDrainFireTargetMovement(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id);
    private bool ResumeFireTargetBenefit(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FireTargetBenefit: { } r } f || f.Id != id) return false;
        AssertFireTargetBenefit(f);
        if (f.PendingMovementContinuation is not null)
        {
            if (!TryDrainFireTargetMovement(f)) ReturnFireTargetBenefitMovement(f);
            return true;
        }
        // Draw and every original child have returned. Fulfil the frozen promise
        // once without rechecking a skill instance lost inside those children.
        if (r.GrantSequence == 0 && _winner == Winner.None && _players[f.OwnerSeat].IsAlive)
        {
            var grant = _turnCardUseEffects.GrantTargetCardQuotaAllowance(r.ActualTurnNumber, r.ActualTurnOwnerSeat,
                f.Id, r.InstructionIndex - 1, r.Source, r.TargetSeat, r.DamageWindowId);
            ReplaceRuntimeTop(f = f with { FireTargetBenefit = r with { GrantSequence = grant.GrantSequence } });
            AdvanceEventRulesAndQueueFact(new TurnTargetCardQuotaAllowanceGrantedEvent(grant));
        }
        ReplaceRuntimeTop(f with { FireTargetBenefit = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "火焰伤害收益已结清，原技能来源已失效。");
        else AdvanceRuntimeProgram(id);
        return true;
    }
    private bool ReturnFireTargetBenefitMovement(ProgramSkillFrame f)
    {
        if (f.FireTargetBenefit is null) return false;
        AssertFireTargetBenefit(f);
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null });
        ResumeFireTargetBenefit(f.Id); return true;
    }
    private bool IsFireTargetBenefitMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.FireTargetBenefit is { } r && effect?.Op == SkillProgramEffectOp.DrawFireTargetAndGrantTurnUseQuota &&
        r.InstructionIndex == f.InstructionIndex && pending.SubjectSeat == r.TargetSeat && pending.CoverageResultBind is null;
    private sealed partial class ProgramSkillHost : IFireTargetBenefitProgramHost
    {
        public SkillProgramStepOutcome DrawFireTargetAndGrantTurnUseQuota(ProgramSkillFrame f) => engine.BeginFireTargetBenefit(f);
        public SkillProgramStepOutcome LoseSkillsAndObtainNamedCard(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginNamedCardAcquisition(f, e);
    }
}
