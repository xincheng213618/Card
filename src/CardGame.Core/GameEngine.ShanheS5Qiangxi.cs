namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome ResolveShanheS5Qiangxi(ProgramSkillFrame supplied, string stage)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (stage == "prepare")
        {
            if (f.InstructionIndex != 1 || f.ShanheS5Qiangxi is not null || f.TriggerId is not null ||
                f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive ||
                _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play || _roundNumber < 1 || _cardUseDebitPhaseInstanceId <= 0)
                throw new InvalidOperationException("S5 Qiangxi lost its original real Play activation and other living target.");
            var prior = ProgramEventHistory<ShanheS5QiangxiPaidEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat &&
                e.Source.SkillId == f.SkillId && e.RoundNumber == _roundNumber);
            var weaponId = f.ActivationId == "discard-weapon" ? f.SelectedCardIds.Single() : (int?)null;
            var from = weaponId is { } id ? _cardZones.GetLocation(id) : (CardLocation?)null;
            if (weaponId is { } weapon && (from!.Value.OwnerSeat != f.OwnerSeat ||
                from.Value.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || EquipmentCatalog.Get(GetAttackCard(weapon).Kind).Slot != EquipmentSlot.Weapon))
                throw new InvalidOperationException("S5 Qiangxi requires its actual selected owner weapon.");
            ReplaceRuntimeTop(f with { ShanheS5Qiangxi = new(new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId),
                f.GameplayHash, target, _roundNumber, _turnNumber, _cardUseDebitPhaseInstanceId, checked(prior + 1),
                _players[f.OwnerSeat].Hp, weaponId, from) });
            return SkillProgramStepOutcome.Continue;
        }
        var r = f.ShanheS5Qiangxi ?? throw new InvalidOperationException("S5 Qiangxi lost its frozen activation.");
        if (f.InstructionIndex != 3 || r.Paid || !HasShanheS5QiangxiPayment(f) ||
            ProgramEventHistory<ShanheS5QiangxiPaidEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("S5 Qiangxi requires one completed real cost on its owning frame.");
        ReplaceRuntimeTop(f = f with { ShanheS5Qiangxi = r with { Paid = true } });
        var damage = checked(r.ActivationCount + 1);
        AdvanceEventRulesAndQueueFact(new ShanheS5QiangxiPaidEvent(f.Id, r.Source, r.GameplayHash, r.TargetSeat,
            r.RoundNumber, r.TurnNumber, r.PhaseInstanceId, r.ActivationCount, damage));
        return _players[r.TargetSeat].IsAlive ? BeginProgramSkillDamage(f, r.TargetSeat, damage) : SkillProgramStepOutcome.Continue;
    }

    private bool HasShanheS5QiangxiPayment(ProgramSkillFrame f)
    {
        if (f.ShanheS5Qiangxi is not { } r) return false;
        if (r.WeaponCardId is not { } id)
            return ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e => e.FrameId == f.Id && e.SkillId == f.SkillId &&
                e.TargetSeat == f.OwnerSeat && e.Amount == 1 && e.RemainingHp == r.CostHpBefore - 1) == 1;
        if (f.SelectedCardPayment is not { InstructionIndex: 2, Operation: SkillProgramEffectOp.DiscardSelected, MovementCommitted: true } paid ||
            !paid.CardIds.SequenceEqual([id]) ||
            f.SelectedCardPaymentResult is not { InstructionIndex: 2, Operation: SkillProgramEffectOp.DiscardSelected, Completed: true }) return false;
        var invoices = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == f.Id &&
            e.InitiatorSeat == f.OwnerSeat && e.ExecutorSeat == f.OwnerSeat && e.SubjectSeat == f.OwnerSeat && e.ActualCount == 1 &&
            e.Reason.Value == $"skill-program.{f.SkillId}.DiscardSelected").ToArray();
        return invoices is [var invoice] && ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(e =>
            e.InvocationId == invoice.InvocationId && e.CardId == id && e.From == r.WeaponFrom &&
            e.CompletionSequence > invoice.SequenceBefore && e.CompletionSequence <= invoice.SequenceAfter) == 1;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 59100)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 59100)]
    private bool ContinuePaidShanheS5Qiangxi(ProgramSkillFrame f) => HasShanheS5QiangxiPayment(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 59100)]
    private void AssertShanheS5Qiangxi(ProgramSkillFrame f)
    {
        if (f.ShanheS5Qiangxi is not { } r) return;
        if (r.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) ||
            r.GameplayHash != f.GameplayHash || r.RoundNumber < 1 || r.TurnNumber < 1 || r.PhaseInstanceId <= 0 || r.ActivationCount < 1 ||
            f.SelectedTargetSeats is not [var target] || r.TargetSeat != target || target == f.OwnerSeat ||
            f.InstructionIndex is < 1 or > 3 || (r.WeaponCardId is null) != (f.ActivationId == "lose-hp") ||
            r.Paid && (ProgramEventHistory<ShanheS5QiangxiPaidEvent>().SingleOrDefault(e => e.FrameId == f.Id) is not { } paid ||
                paid.Source != r.Source || paid.GameplayHash != r.GameplayHash || paid.ActivationCount != r.ActivationCount ||
                paid.Damage != r.ActivationCount + 1 || !HasShanheS5QiangxiPayment(f)))
            throw new InvalidOperationException("S5 Qiangxi lost its exact owner, paid source, round count or target.");
    }

    private sealed partial class ProgramSkillHost : IShanheS5QiangxiHost
    {
        public SkillProgramStepOutcome ResolveShanheS5Qiangxi(ProgramSkillFrame frame, string stage) => engine.ResolveShanheS5Qiangxi(frame, stage);
    }
}
