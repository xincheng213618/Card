namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsWushuangDyingExitCandidate(ProgramTriggerCandidate c, int victim) => IsWushuangMode && c.OwnerSeat == victim &&
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveWushuangCombatTactic, Amount: 2174 or 2175 } e] &&
        c.SkillId == $"wushuang:tactic-{e.Amount}";

    private bool IsWushuangDyingExitContext(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.DyingExited || context.SourceSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat ||
            !IsWushuangDyingExitCandidate(c, c.OwnerSeat) || !_players[c.OwnerSeat].IsAlive || _players[c.OwnerSeat].Hp <= 0 ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.DyingExited, Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit, YanPuDyingExit: { } ret } parent ||
            parent.OwnerSeat != c.OwnerSeat || ret.VictimSeat != c.OwnerSeat || ret.Round != _roundNumber || ret.Turn != _turnNumber || ret.TurnOwner != _currentSeat ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c) return false;
        var index = _resolutionStack.FindIndex(w => w.Id == parent.Id);
        return index > 0 && _resolutionStack[index - 1].Id == ret.DyingParentFrameId &&
            ProgramEventHistory<DyingResolvedEvent>().Count(e => e.ResolutionId == ret.DyingFrameId && e.VictimSeat == ret.VictimSeat && e.Survived) == 1;
    }

    private bool IsExactWushuangDyingExitProgram(ProgramSkillFrame f) => f.WushuangCombat is { TacticId: 2174 or 2175 } &&
        IsWushuangDyingExitReturn(f);

    private bool IsWushuangDyingExitReturn(ProgramSkillFrame f) =>
        f.WindowContext is { } context &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) && IsWushuangDyingExitContext(parent.Candidates[parent.CandidateIndex], context);

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 61400)]
    private bool AllowsWushuangDyingExitDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess) =>
        !sourceLess && source is null && nature == DamageNature.Normal && f.AttackAttempt is null && IsExactWushuangDyingExitProgram(f) &&
        f.WushuangCombat is { Stage: 3, Index: > 0 } r && r.Index <= r.Seats.Count && r.Seats[r.Index - 1] == target && amount == r.Amount;

    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 61400)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 61400)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 61400)]
    private bool HasWushuangDyingExitDamageObserver(long id)
    {
        var root = _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(IsExactWushuangDyingExitProgram);
        return root is not null && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame) >
            _resolutionStack.FindIndex(f => f.Id == root.Id);
    }
}
