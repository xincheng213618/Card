namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5XiongluanId = "shanhe:s5-xiongluan-14116";
    private const string ShanheXiongluanJudgmentReason = "shanhe.s5.xiongluan.abolish-judgment";

    [ProgramCapability]
    private sealed class ShanheS5ZhangxiuCapability(GameEngine engine) : IShanheS5ZhangxiuHost
    {
        public SkillProgramStepOutcome PayShanheS5Xiongluan(ProgramSkillFrame f) => engine.PayShanheS5Xiongluan(f);
        public void ResolveShanheLeizhenUse(ProgramSkillFrame f) => engine.ResolveShanheLeizhenUse(f);
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 59401)]
    private bool CanPayShanheS5Xiongluan(CharacterState owner, SkillProgramActivation activation) =>
        !activation.Effects.Any(e => e.Op == SkillProgramEffectOp.PayShanheS5Xiongluan) ||
        owner.IsAlive && Enum.GetValues<EquipmentSlot>().Any(slot => owner.EquipmentSlotCapacity(slot) > 0);

    private SkillProgramStepOutcome PayShanheS5Xiongluan(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id); var owner = _players[f.OwnerSeat];
        if (f.SkillId != ShanheS5XiongluanId || f.TriggerId is not null || f.InstructionIndex != 1 ||
            f.ShanheS5Xiongluan is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            !owner.IsAlive || f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive)
            throw new InvalidOperationException("S5 Xiongluan needs one real other target in its original own Play activation.");
        var slots = Enum.GetValues<EquipmentSlot>().Where(slot => owner.EquipmentSlotCapacity(slot) > 0).Order().ToArray();
        if (slots.Length == 0) throw new InvalidOperationException("S5 Xiongluan requires a remaining actual equipment slot.");
        var beforeRandom = _random.State; var chosen = slots[_random.Next(slots.Length)]; var afterRandom = _random.State;
        var receipt = new ShanheS5XiongluanPayment(new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, target, _turnNumber, _cardUseDebitPhaseInstanceId, chosen, slots.Length,
            beforeRandom, afterRandom, owner.JudgmentAreaAbolished, _movementSequence, _movementSequence, false);
        ReplaceRuntimeTop(f = f with { ShanheS5Xiongluan = receipt, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (!owner.JudgmentAreaAbolished) AbolishProgramOwnerAreas(f, [CardZoneKind.Judgment]);
        SetEquipmentSlotCapacity(owner, chosen, 0);
        DiscardProgramOwnedZoneCards(f, [CardZoneKind.Judgment], new(ShanheXiongluanJudgmentReason));
        f = GetActiveProgramFrame(f.Id);
        receipt = receipt with { Paid = true, MovementAfter = _movementSequence };
        ReplaceRuntimeTop(f = f with { ShanheS5Xiongluan = receipt });
        AdvanceEventRulesAndQueueFact(new ShanheS5XiongluanCostPaidEvent(f.Id, receipt.Source, receipt.GameplayHash,
            target, receipt.ActualTurnNumber, receipt.PhaseInstanceId, chosen, slots.Length,
            receipt.JudgmentWasAbolished, receipt.MovementBefore, receipt.MovementAfter));
        AdvanceRuntimeProgram(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 59401)]
    private bool ResumeShanheS5Xiongluan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShanheS5Xiongluan: { Paid: true, ChildrenCompleted: false } r } f ||
            f.Id != id) return false;
        AssertShanheS5Xiongluan(f);
        if (TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id)) return true;
        ReplaceRuntimeTop(f with { ShanheS5Xiongluan = r with { ChildrenCompleted = true }, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(id); return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 59401)]
    private bool ReturnShanheS5XiongluanMovement(ProgramSkillFrame f) =>
        f.ShanheS5Xiongluan is { ChildrenCompleted: false } && ResumeShanheS5Xiongluan(f.Id);

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 59401)]
    private bool IsShanheS5XiongluanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (effect?.Op != SkillProgramEffectOp.PayShanheS5Xiongluan ||
            f.ShanheS5Xiongluan is not { Paid: true, ChildrenCompleted: false } ||
            pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        AssertShanheS5Xiongluan(f); return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 59401)]
    private void AssertShanheS5Xiongluan(ProgramSkillFrame f)
    {
        if (f.ShanheS5Xiongluan is not { } r) return;
        if (!r.Paid || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            f.SkillId != ShanheS5XiongluanId || r.GameplayHash != f.GameplayHash || f.TriggerId is not null ||
            r.ActualTurnNumber != _turnNumber || r.PhaseInstanceId != _cardUseDebitPhaseInstanceId ||
            r.CandidateCount is < 1 or > 5 || !Enum.IsDefined(r.Slot) || r.RandomBefore == r.RandomAfter ||
            r.MovementBefore < 0 || r.MovementAfter < r.MovementBefore || r.MovementAfter > _movementSequence ||
            f.SelectedTargetSeats is not [var target] || target != r.TargetSeat || target == f.OwnerSeat ||
            ProgramEventHistory<ShanheS5XiongluanCostPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid.Source != r.Source || paid.GameplayHash != r.GameplayHash || paid.TargetSeat != target ||
            paid.ActualTurnNumber != r.ActualTurnNumber || paid.PhaseInstanceId != r.PhaseInstanceId ||
            paid.Slot != r.Slot || paid.CandidateCount != r.CandidateCount || paid.JudgmentWasAbolished != r.JudgmentWasAbolished ||
            paid.MovementBefore != r.MovementBefore || paid.MovementAfter != r.MovementAfter ||
            (r.ChildrenCompleted ? f.PendingMovementContinuation is not null :
                f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat))
            throw new InvalidOperationException("S5 Xiongluan lost its original private random payment, actual area cost or typed movement continuation.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 59401)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 59401)]
    private bool ContinuePaidShanheS5Xiongluan(ProgramSkillFrame f)
    {
        if (f.ShanheS5Xiongluan is not { Paid: true }) return false;
        AssertShanheS5Xiongluan(f); return true;
    }
}
