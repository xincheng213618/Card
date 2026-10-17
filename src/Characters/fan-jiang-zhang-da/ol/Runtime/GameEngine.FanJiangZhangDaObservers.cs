namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 27350)]
    private void AssertJuesheng(ProgramSkillFrame f)
    {
        if (f.Juesheng is not { } r) return; var o = r.Origin;
        if (f.SkillId != "ol:juesheng" || f.ActivationId != "limited-duel" || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || r.InstructionIndex != 1 ||
            o.ProgramFrameId != f.Id || o.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) || o.GameplayHash != f.GameplayHash || o.ActorSeat != f.OwnerSeat ||
            o.ActualTurn != _turnNumber || f.SelectedCardIds.Count != 0 || !f.SelectedTargetSeats.SequenceEqual([o.InitialTargetSeat]) ||
            ProgramEventHistory<JueshengSpentEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId) != 1 ||
            ProgramEventHistory<JueshengDuelIssuedEvent>().Count(e => e.Origin == o && e.InstructionIndex == r.InstructionIndex) != 1 || r.GrantCursor < 0 || r.GrantCursor > r.FinalTargets.Count ||
            r.Targets.Select(t => t.TargetSeat).Distinct().Count() != r.Targets.Count || r.Targets.Any(t => t.SlashCount < 0 || t.Amount != Math.Max(1, t.SlashCount)) ||
            !ProgramEventHistory<JueshengTargetDamageFrozenEvent>().Where(e => e.FrameId == f.Id && e.UseId == o.UseId).OrderBy(e => e.Index).Select(e => new JueshengTargetDamage(e.TargetSeat, e.SlashCount, e.Amount)).SequenceEqual(r.Targets) ||
            r.FinalTargets.Distinct().Count() != r.FinalTargets.Count || !ProgramEventHistory<JueshengFinalTargetEvent>().Where(e => e.FrameId == f.Id && e.UseId == o.UseId).OrderBy(e => e.Index).Select(e => e.TargetSeat).SequenceEqual(r.FinalTargets))
            throw new InvalidOperationException("Juesheng lost its exact permanent spend, immutable native target damage invoices or then-clause cursor.");
        if (r.NativeCompleted ? LifecycleCardUse(o.UseId) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == o.UseId) != 1 : LifecycleCardUse(o.UseId) is not { } use || !IsJueshengDuelUse(use.Id))
            throw new InvalidOperationException("Juesheng must preserve its exact whole native Use or one completed native return.");
        if (r.Stage == JueshengStage.GrantChildren && !r.NativeCompleted || f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Juesheng's issued grants remain on their owning receipt and resume through the program continuation.");
        var processed = ProgramEventHistory<JueshengSkillGrantedEvent>().Where(e => e.FrameId == f.Id).Select(e => (e.Index, e.RecipientSeat))
            .Concat(ProgramEventHistory<JueshengGrantSkippedEvent>().Where(e => e.FrameId == f.Id).Select(e => (e.Index, e.RecipientSeat))).OrderBy(e => e.Index).ToArray();
        if (processed.Length != r.GrantCursor || processed.Where((e, i) => e.Index != i || e.RecipientSeat != r.FinalTargets[i]).Any())
            throw new InvalidOperationException("Juesheng cannot pay or skip the same recipient twice.");
    }
    private bool FanJiangZhangDaFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Juesheng is not { } r) return false; AssertJuesheng(f);
        if (child is CardUseFrame use) return r.Stage == JueshengStage.NativeUse && use.JueshengDuel == r.Origin && IsJueshengDuelUse(use.Id);
        return r.Stage == JueshengStage.GrantChildren && child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } changed && changed.ResumeProgramFrameId == f.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? FanJiangZhangDaObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.FanJiangZhangDaFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i,
        static (x, n) => x.IsMovementObserverEdge(n) || x.IsYanPuDyingExitEdge(n) || RecoveryReplacementFrameRidesOn(x._resolutionStack[n], x._resolutionStack[n - 1]),
        static (x, n, d) => x.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    private bool HasFanJiangZhangDaDamageObserver(long id) => FanJiangZhangDaObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasFanJiangZhangDaBeforeDamageObserver(long id) => FanJiangZhangDaObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsFanJiangZhangDaDying() => ActiveDying is { } d && FanJiangZhangDaObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsFanJiangZhangDaNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || FanJiangZhangDaObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f, reference) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27350)]
    private bool AdvanceFanJiangZhangDaSubtree() => _pendingDecision is null && FanJiangZhangDaObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
