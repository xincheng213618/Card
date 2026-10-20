namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureVirtualDuelLegacy, 32900)]
    private void CaptureMouLzDirect(ProgramSkillFrame supplied, int actor, int target)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuZhiSibingPress)) return;
        var f = GetActiveProgramFrame(supplied.Id);
        if (FrozenProgramDuelInstructionIndex(f.Id) != f.InstructionIndex || f.CardAttack is not { Active: true, EffectiveCardKind: CardKind.Duel, CardId: null, PhysicalCardIds.Count: 0 } a || a.CardUserSeat != actor || a.TargetSeat != target || f.Continuations.Duel is not { Active: true }) throw new InvalidOperationException("Sibing direct-Duel capture requires the original native producer instruction.");
        var d = new MouLuZhiOlDirectDuel(f.Id, f.InstructionIndex, new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, actor, target);
        ReplaceRuntimeFrame(f.Id, f with { MouLuZhiOlDirectDuel = d }); AdvanceEventRulesAndQueueFact(new MouLuZhiOlDirectDeclaredEvent(d));
    }
    private bool ValidMouLzDirect(ProgramSkillFrame p, MouLuZhiOlDirectDuel d) => p.Id == d.FrameId && p.InstructionIndex == d.Instruction && p.GameplayHash == d.Hash && d.Producer == new CardConversionSource(p.SkillId, GetProgramBindingId(p), p.OwnerSeat, p.SkillInstanceId) &&
        FrozenProgramDuelInstructionIndex(p.Id) == d.Instruction && p.CardAttack is { EffectiveCardKind: CardKind.Duel, CardId: null, PhysicalCardIds.Count: 0 } a && a.CardUserSeat == d.Actor &&
        (p.TriggerId is null ? p.SelectedTargetSeats.SequenceEqual([d.Actor, d.Target]) : p.OwnerSeat == d.Actor && p.SelectedTargetSeats.SequenceEqual([d.Target])) && ProgramEventHistory<MouLuZhiOlDirectDeclaredEvent>().Count(e => e.Duel == d with { PressClosed = false, Returned = false, CounterClosed = false, DamageApplied = false, FinalTarget = 0 }) == 1;
    private bool TryBeginMouLzDirectDesignation(long producerId)
    {
        var f = GetActiveProgramFrame(producerId);
        if (f.MouLuZhiOlDirectDuel is not { PressClosed: false } d) return false;
        if (!ValidMouLzDirect(f, d)) throw new InvalidOperationException("Sibing lost the native Duel designation identity.");
        return OpenMouLzDirectWindow(f, d, completed: false);
    }
    private bool OpenMouLzDirectWindow(ProgramSkillFrame p, MouLuZhiOlDirectDuel d, bool completed)
    {
        var owner = completed ? d.Target : d.Actor; var binding = completed ? "undamaged-retaliation" : "red-press";
        var instance = GetSkillBindingShard(_players[owner]).ProgramInstances.FirstOrDefault(i => i.SkillId == MouLuZhiOlComposition.Sibing);
        var available = _winner == Winner.None && _players[owner].IsAlive && instance is not null &&
            (completed ? !ProgramEventHistory<MouLuZhiOlUseDamageEvent>().Any(e => e.OriginalId == d.FrameId && e.Instruction == d.Instruction && e.Target == owner && e.Amount > 0) && MouLzCards(owner, false, false).Count > 0 && MouLzSlashTargets(owner).Length > 0 : MouLzPressAvailable(owner));
        if (!available)
        { ReplaceRuntimeFrame(p.Id, p with { MouLuZhiOlDirectDuel = completed ? d with { CounterClosed = true } : d with { PressClosed = true } }); return false; }
        var t = instance!.Program.Triggers.Single(x => x.Id == binding); var c = new ProgramTriggerCandidate(owner, instance.SkillId, binding, instance.SkillInstanceId, instance.Program.GameplayHash, t.Priority);
        var id = ++_resolutionSequence; var w = new ProgramSkillWindowContext(t.Window, id, owner, SourceSeat: d.Actor, TargetSeat: d.Target);
        var frame = new MouLuZhiOlDirectWindowFrame(id, p.Id, completed, c, w); PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new MouLuZhiOlDirectWindowEvent(id, p.Id, d.Instruction, completed, new(c.SkillId, c.BindingId, owner, c.SkillInstanceId), c.GameplayHash)); AdvanceMouLzDirectWindow(frame); return true;
    }
    [NativeFrameRoute(typeof(MouLuZhiOlDirectWindowFrame), 32900)]
    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 32900)]
    private void AdvanceMouLzDirectWindow(MouLuZhiOlDirectWindowFrame w)
    { if (!ExactMouLzDirectWindow(w) || !IsMouLzDirectCandidate(w.Candidate, w.Context)) throw new InvalidOperationException("Sibing direct native window lost its live accepted source."); BeginProgramBinding(w.Candidate, w.Context); }
    private bool IsMouLzDirectCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        return context.CardUse is null && _resolutionStack.SingleOrDefault(x => x.Id == context.ParentFrameId) is MouLuZhiOlDirectWindowFrame w && w.Candidate == c && w.Context == context && ExactMouLzDirectWindow(w) &&
            HasRuntimeSkillInstance(_players[c.OwnerSeat], c.SkillId, c.SkillInstanceId) && _players[c.OwnerSeat].IsAlive;
    }
    private bool ExactMouLzDirectWindow(MouLuZhiOlDirectWindowFrame w)
    {
        var c = w.Candidate; var context = w.Context;
        if (context.CardUse is not null || context.ParentFrameId != w.Id || context.OwnerSeat != c.OwnerSeat ||
            _resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramSkillFrame { MouLuZhiOlDirectDuel: { } d } p || !ValidMouLzDirect(p, d) ||
            c.SkillId != MouLuZhiOlComposition.Sibing || c.OwnerSeat != (w.Completed ? d.Target : d.Actor) || c.BindingId != (w.Completed ? "undamaged-retaliation" : "red-press") ||
            c.GameplayHash != _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash || context.Window != (w.Completed ? SkillProgramTriggerWindow.CardUseCompleted : SkillProgramTriggerWindow.CardUseTargetsFinalized) ||
            ProgramEventHistory<MouLuZhiOlDirectWindowEvent>().Count(e => e.WindowId == w.Id && e.ProducerId == p.Id && e.Instruction == d.Instruction && e.Completed == w.Completed) != 1) return false;
        return w.Completed ? d.Returned && !d.CounterClosed && p.CardAttack is { Active: false } : !d.PressClosed && !d.Returned && p.CardAttack is { Active: true };
    }
    [NativeAttackReturn(NativeAttackReturnPhase.AfterCleanup, 32900)] private bool ReturnMouLzDirect(AttackCompletionReceipt c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuZhiOlDirectDuel: { Returned: false } d } p || c.ResolutionId != p.Id || c.ProgramFrameId != p.Id) return false;
        if (!ValidMouLzDirect(p, d) || p.CardAttack is not { Active: false } || c.DelayedTurn || c.RequestedSlashChild) throw new InvalidOperationException("Sibing direct Duel must return its genuine resolved producer attack.");
        d = d with { Returned = true, DamageApplied = c.DamageWasApplied, FinalTarget = c.FinalTargetSeat }; ReplaceRuntimeFrame(p.Id, p = p with { MouLuZhiOlDirectDuel = d }); return OpenMouLzDirectWindow(p, d, true);
    }
    [ProgramPipelineHook(ProgramPipelinePhase.ReturnCompletedProgramBinding, 32900)] private bool ReturnMouLzDirectBinding(ProgramSkillFrame f, bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not MouLuZhiOlDirectWindowFrame w || f.WindowContext?.ParentFrameId != w.Id) return false;
        if (f.SkillId != w.Candidate.SkillId || f.TriggerId != w.Candidate.BindingId || f.OwnerSeat != w.Candidate.OwnerSeat || f.SkillInstanceId != w.Candidate.SkillInstanceId || f.GameplayHash != w.Candidate.GameplayHash || !ExactMouLzDirectWindow(w)) throw new InvalidOperationException("Sibing direct window cannot resume another native producer.");
        PopResolutionFrame(w.Id, ResolutionFrameKind.MouLuZhiOlDirectWindow); var p = GetActiveProgramFrame(w.ParentFrameId); var d = p.MouLuZhiOlDirectDuel!;
        ReplaceRuntimeTop(p with { MouLuZhiOlDirectDuel = w.Completed ? d with { CounterClosed = true } : d with { PressClosed = true } }); AdvanceEventRulesAndQueueFact(new MouLuZhiOlDirectResumedEvent(w.Id, p.Id, w.Completed));
        if (_winner != Winner.None) return true;
        if (w.Completed) CompleteAttackAfterCardResolution(new(p.Id, false, p.Id, null, DamageWasApplied: d.DamageApplied, FinalTargetSeat: d.FinalTarget));
        else BeginDuelResponse(ActiveDuel ?? throw new InvalidOperationException("Sibing lost its still-unresolved direct Duel.")); return true;
    }
    private bool HasMouLzDirectUseContinuation(long id)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == id && x is ProgramSkillFrame { MouLuZhiOlDirectDuel: { Returned: false } });
        if (n < 0 || n + 2 >= _resolutionStack.Count || _resolutionStack[n + 1] is not MouLuZhiOlDirectWindowFrame { Completed: false } w || w.ParentFrameId != id || !ExactMouLzDirectWindow(w) || _resolutionStack[n + 2] is not ProgramSkillFrame f || f.WindowContext != w.Context || !MountObserverCandidateMatches(f, w.Candidate)) return false;
        return n + 2 == _resolutionStack.Count - 1 && ValidMouLz(f) || MouLzSuffix(n + 2);
    }
}
