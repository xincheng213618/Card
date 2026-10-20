namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29200)]
    private bool KeepPaidMouLuMeng(ProgramSkillFrame f) =>
        f.MouLuMengKeji is not null && ValidMouKeji(f) || f.MouLuMengDujiang is not null && ValidMouDujiang(f) ||
        f.MouLuMengDuojing is { Stage: MouLuMengDuojingStage.Taking or MouLuMengDuojingStage.MovementChildren or MouLuMengDuojingStage.Complete } && ValidMouDuojing(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29200)]
    private void AssertMouLuMeng(ProgramSkillFrame f)
    {
        if (f.MouLuMengKeji is not null && !ValidMouKeji(f) || f.MouLuMengDujiang is not null && !ValidMouDujiang(f) ||
            f.MouLuMengDuojing is not null && !ValidMouDuojing(f))
            throw new InvalidOperationException("Mobile Lu Meng lost its exact native cost, actual awakening, finalized Slash or once-paid target receipt.");
        if (f.MouLuMengDuojing is not { } r || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            r.Stage is not (MouLuMengDuojingStage.Choosing or MouLuMengDuojingStage.Taking)) return;
        var choices = r.Stage == MouLuMengDuojingStage.Choosing ? MouDuojingChoices(f) : MouDuojingTakeChoices(f);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat ||
            p.TargetSeat != r.Targets[r.TargetIndex] || !AssistedChoicesEqual(p.Choices, choices))
            throw new InvalidOperationException("Duojing's current target must retain its canonical payment or concealed native Hand choice.");
    }
    private bool MouLuMengObserverOrigin(ProgramSkillFrame f) =>
        f.MouLuMengKeji is not null && ValidMouKeji(f) || f.MouLuMengDujiang is not null && ValidMouDujiang(f) ||
        f.MouLuMengDuojing is { Stage: MouLuMengDuojingStage.MovementChildren } && ValidMouDuojing(f);
    private bool MouLuMengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouLuMengObserverOrigin(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f) || RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills)
            return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is HpChangedTriggerWindowFrame hp)
            return ProgramHpReturnMatches(f, hp) && (f.MouLuMengKeji is null ||
                hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 && f.InstructionIndex == 2);
        if (child is DyingFrame dying)
            return f.MouLuMengKeji is not null && f.ActivationId == "lose-hp" && f.InstructionIndex == 2 &&
                dying.ParentFrameId == f.Id && dying.VictimSeat == f.OwnerSeat && dying.Continuation == DyingContinuationKind.ProgramSkill && MouKejiCostWasPaid(f);
        if (child is not CardsMovedTriggerWindowFrame moved || !ProgramMovementReturnMatches(f, moved)) return false;
        if (f.MouLuMengDuojing is { Stage: MouLuMengDuojingStage.MovementChildren } r)
            return r.TakenCardId is { } card && r.MovementSequence is { } sequence && moved.Batch.Movements.All(m =>
                _cardMovements.Contains(m) && m.CardId == card && m.Sequence == sequence && m.From == CardLocation.Hand(r.Targets[r.TargetIndex]) &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == MouDuojingTakeReason);
        return f.MouLuMengKeji is { } cost && f.ActivationId == "discard-hand" && f.InstructionIndex == 2 &&
            f.SelectedCardPayment is { MovementCommitted: true } payment && payment.CardIds.SequenceEqual(f.SelectedCardIds) &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > cost.MovementBefore &&
                (m.CardId == cost.SelectedCardId || GetProgramDiscardSource(m)?.OwnerSeat == f.OwnerSeat) &&
                (m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}" ||
                 m.Reason.Value.StartsWith($"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}-", StringComparison.Ordinal)));
    }
    private bool MouLuMengObserverSuffix(int index) => HasObserverSuffix(index, static (g, child) =>
        g.IsMovementObserverEdge(child) || g.IsSameNameHandObserverEdge(child) || g.IsYanPuDyingExitEdge(child) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[child - 1], g._resolutionStack[child]) ||
        g._resolutionStack[child - 1] is ProgramSkillFrame parent && g.MouLuMengFirstChild(parent, g._resolutionStack[child]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]) ||
        g.RecoveryReplacementFrameRidesOn(g._resolutionStack[child], g._resolutionStack[child - 1]),
        static (g, child, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(child, dying));
    private ProgramSkillFrame? MouLuMengObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouLuMengFirstChild(f, child),
        static (g, index) => g.MouLuMengObserverSuffix(index));
    private bool HasMouLuMengDamageObserver(long id) => MouLuMengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouLuMengBeforeDamageObserver(long id) => MouLuMengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouLuMengDying() => ActiveDying is { } dying && MouLuMengObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouLuMengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouLuMengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouLuMengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!MouLuMengObserverOrigin(f) || recovery.ParentFrameId == f.Id) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouLuMengFirstChild(f, _resolutionStack[parent + 1]) &&
            MouLuMengObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29200)]
    private bool AdvanceMouLuMengChildren() => _pendingDecision is null && MouLuMengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
