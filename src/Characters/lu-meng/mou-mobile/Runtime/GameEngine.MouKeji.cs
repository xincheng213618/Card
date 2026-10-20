namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouLuMengOwnDying(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
        _resolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == owner && f.Step != ResolutionFrameStep.Completed);
    private bool IsMouLuMengPeachForbidden(int actor, CardKind kind, CardActionType type) => kind == CardKind.Peach &&
        (type == CardActionType.Use || type == CardActionType.Response && IsProgramResponseCardUse(_players[actor], kind)) &&
        HasRuntimeSkill(_players[actor], MouKeji) && !MouLuMengOwnDying(actor);
    private bool IsMouLuMengPeachRescueAllowed(int actor) => !HasRuntimeSkill(_players[actor], MouKeji) || MouLuMengOwnDying(actor);
    private int MouLuMengArmorHandLimit(CharacterState owner) => HasRuntimeSkill(owner, MouKeji) ? owner.Armor : 0;
    private int MouKejiPhaseUses(int owner) => _programPhaseUses.GetValueOrDefault((owner, MouKeji, "discard-hand")) +
        _programPhaseUses.GetValueOrDefault((owner, MouKeji, "lose-hp"));
    [ProgramPhaseUsedProjection(10)]
    private int MouLuMengProjectedPhaseUses(int owner, string skill, int ordinary) =>
        skill == MouKeji && MouLuMengAwakened(owner) ? MouKejiPhaseUses(owner) : ordinary;

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 29200, SkillProgramEffectOp.ResolveMouLuMengKeji)]
    private bool CanActivateMouKeji(ProgramActivationLegalityContext c) => c.SkillId == MouKeji && MouLuMengComposition.IsKeji(c.Program) &&
        c.Owner.IsAlive && _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && phase.ActorSeat == c.OwnerSeat &&
        (!MouLuMengAwakened(c.OwnerSeat) || MouKejiPhaseUses(c.OwnerSeat) < 1);
    private bool MouKejiRoot(ProgramSkillFrame f) => f.SkillId == MouKeji && f.TriggerId is null && f.WindowContext is null &&
        f.ActivationId is "discard-hand" or "lose-hp" && f.SelectedTargetSeats.Count == 0 &&
        (f.ActivationId == "discard-hand" ? f.SelectedCardIds is [> 0] : f.SelectedCardIds.Count == 0) &&
        _contentRegistry.GetSkill(f.SkillId).Program is { } p && p.GameplayHash == f.GameplayHash && MouLuMengComposition.IsKeji(p);
    private bool ValidMouKeji(ProgramSkillFrame f) => MouKejiRoot(f) && f.MouLuMengKeji is { } r &&
        r.Source == MouLuMengSource(f) && r.GameplayHash == f.GameplayHash && r.Phase.ActorSeat == f.OwnerSeat &&
        r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play && IsRecordedActualDiscardRecoveryPhase(r.Phase) &&
        r.SelectedCardId == (f.SelectedCardIds.Count == 1 ? f.SelectedCardIds[0] : (int?)null) &&
        ProgramEventHistory<MouLuMengKejiReservedEvent>().Count(e => e == new MouLuMengKejiReservedEvent(f.Id, r.Source, r.GameplayHash, r.Phase)) == 1 &&
        (!r.CostReturned || ProgramEventHistory<MouLuMengKejiCostReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var cost] &&
            cost.Branch == f.ActivationId && cost.Paid == r.Paid);
    private bool MouKejiCostWasPaid(ProgramSkillFrame f)
    {
        if (!ValidMouKeji(f)) return false;
        var r = f.MouLuMengKeji!;
        if (f.ActivationId == "lose-hp")
            return ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == f.OwnerSeat && e.Amount == 1) == 1;
        return f.SelectedCardPayment is { InstructionIndex: 2, Operation: SkillProgramEffectOp.DiscardSelected, MovementCommitted: true } payment &&
            payment.RecipientSeat == f.OwnerSeat && payment.CardIds.SequenceEqual(f.SelectedCardIds) &&
            ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == f.Id &&
                e.InitiatorSeat == f.OwnerSeat && e.ExecutorSeat == f.OwnerSeat && e.SubjectSeat == f.OwnerSeat &&
                e.ActualCount == 1 && e.HandCount == 1 && e.SequenceBefore >= r.MovementBefore &&
                e.TurnNumber == r.Phase.TurnNumber && e.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}") == 1;
    }
    private SkillProgramStepOutcome ResolveMouKeji(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouKejiRoot(f)) throw new InvalidOperationException("Keji needs its exact complete native Hand-discard or HP-loss activation.");
        if (f.InstructionIndex == 1)
        {
            var phase = MouLuMengPhase();
            if (f.MouLuMengKeji is not null || phase.Kind != ActualDiscardRecoveryPhaseKind.Play || phase.ActorSeat != f.OwnerSeat ||
                _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || !_players[f.OwnerSeat].IsAlive ||
                MouLuMengAwakened(f.OwnerSeat) && MouKejiPhaseUses(f.OwnerSeat) != 1 ||
                f.SelectedCardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat)))
                throw new InvalidOperationException("Keji cannot reuse its phase allowance or replace its selected owned Hand material.");
            var r = new MouLuMengKejiReceipt(MouLuMengSource(f), f.GameplayHash, phase,
                f.SelectedCardIds.Count == 1 ? f.SelectedCardIds[0] : null, _movementSequence);
            ReplaceRuntimeTop(f with { MouLuMengKeji = r });
            AdvanceEventRulesAndQueueFact(new MouLuMengKejiReservedEvent(f.Id, r.Source, r.GameplayHash, phase));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 3 || !ValidMouKeji(f) || f.MouLuMengKeji is not { CostReturned: false } receipt ||
            f.SelectedCardPayment is not null && f.SelectedCardPaymentResult is not { Completed: true })
            throw new InvalidOperationException("Keji's armor must follow the returned actual native cost and its children.");
        var paid = MouKejiCostWasPaid(f);
        ReplaceRuntimeTop(f = f with { MouLuMengKeji = receipt with { CostReturned = true, Paid = paid } });
        AdvanceEventRulesAndQueueFact(new MouLuMengKejiCostReturnedEvent(f.Id, f.ActivationId, paid));
        if (paid) return SkillProgramStepOutcome.Continue;
        CancelProgramBindingAndCleanup(f, "克己未实际支付本次弃牌或体力流失，不获得护甲。");
        return SkillProgramStepOutcome.AwaitChild;
    }
    // The original selected-card movement owns payment and its returned child
    // cursor. Losing the source after that payment cannot cancel the armor tail.
    [ProgramContinuation(ProgramContinuationKind.Movement, 29200)]
    private bool ReturnMouKejiDiscard(ProgramSkillFrame f)
    {
        if (f.MouLuMengKeji is null || f.SelectedCardPaymentResult is not null) return false;
        if (!ValidMouKeji(f) || f.ActivationId != "discard-hand" || f.InstructionIndex != 2 ||
            f.SelectedCardPayment is not { Operation: SkillProgramEffectOp.DiscardSelected, InstructionIndex: 2,
                MovementCommitted: true, ActiveChildFrameId: null } payment || f.PendingMovementContinuation is not null ||
            !payment.CardIds.SequenceEqual(f.SelectedCardIds))
            throw new InvalidOperationException("Keji cannot return an uncommitted or unrelated native discard payment.");
        var alive = _players[f.OwnerSeat].IsAlive;
        ReplaceRuntimeTop(f with { SelectedCardPaymentResult = new(payment.InstructionIndex, payment.Operation, payment.LastCompletedChildFrameId, alive) });
        if (!alive) CancelProgramBindingAndCleanup(GetActiveProgramFrame(f.Id), "克己的实际弃牌已经完成，阵亡角色不再获得护甲。");
        else AdvanceRuntimeProgram(f.Id);
        return true;
    }
}
