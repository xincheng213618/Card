namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26101)]
    private bool CanOfferLuanfeng(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLuanfengDyingRecovery) ||
        context is { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } &&
        ActiveDying is { } dying && dying.VictimSeat == victim && _players[victim].IsAlive && _players[victim].Hp <= 0 &&
        _players[victim].MaxHp >= _players[candidate.OwnerSeat].MaxHp;
    [ProgramCapability]
    private sealed class WoLongFengChuProgramCapability(GameEngine engine) : IWoLongFengChuProgramHost
    { public SkillProgramStepOutcome ResolveLuanfeng(ProgramSkillFrame frame) => engine.ResolveLuanfeng(frame); }
    private SkillProgramStepOutcome ResolveLuanfeng(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.Luanfeng is not null || frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: { } victim } ||
            ActiveDying is not { } dying || dying.VictimSeat != victim || !_players[victim].IsAlive || _players[victim].Hp > 0 ||
            _players[victim].MaxHp < _players[frame.OwnerSeat].MaxHp)
            throw new InvalidOperationException("Luanfeng requires the exact current dying victim and its qualifying maximum HP.");
        var receipt = new LuanfengReceipt(frame.Id, new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
            frame.GameplayHash, dying.FrameId, victim, LuanfengStage.RecoveryChild, Math.Min(3, _players[victim].MaxHp) - _players[victim].Hp);
        ReplaceRuntimeTop(frame with { Luanfeng = receipt });
        AdvanceEventRulesAndQueueFact(new LuanfengIssuedEvent(frame.Id, receipt.Source, dying.FrameId, victim));
        new ProgramSkillHost(this).Recover(frame.Id, frame.OwnerSeat, victim, 3 - _players[victim].Hp, null, null);
        if (_resolutionStack.LastOrDefault()?.Id == frame.Id) ResumeLuanfeng(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26101)]
    private bool ResumeLuanfeng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Luanfeng: { } receipt } frame || frame.Id != id) return false;
        if (DrainEquipmentSlotNamedChildren(frame.Id)) return true;
        frame = GetActiveProgramFrame(id); receipt = frame.Luanfeng!;
        var victim = _players[receipt.VictimSeat];
        if (!victim.IsAlive) { ReplaceRuntimeTop(frame with { Luanfeng = null }); AdvanceRuntimeProgram(id); return true; }
        if (receipt.Stage == LuanfengStage.RecoveryChild)
        {
            var restored = Enum.GetValues<EquipmentSlot>().Count(slot => RestoreEquipmentSlot(victim, slot));
            var draw = Math.Max(0, 6 - restored - GetHand(victim).Count);
            receipt = receipt with { Stage = LuanfengStage.DrawChildren, RestoredSlots = restored, RequestedDraw = draw, Before = _movementSequence, After = _movementSequence };
            ReplaceRuntimeTop(frame = frame with { Luanfeng = receipt, PendingMovementContinuation = draw > 0 ? new(frame.OwnerSeat, 0, null) : null });
            AdvanceEventRulesAndQueueFact(new LuanfengSlotsRestoredEvent(frame.Id, victim.Seat, restored, draw));
            if (draw > 0)
            {
                DrawProgramCards(frame.Id, victim.Seat, draw, null, null, SkillProgramCardSetVisibility.Private, new("program.luanfeng.draw-to"));
                ReplaceRuntimeTop(frame = GetActiveProgramFrame(id) with { Luanfeng = receipt with { After = _movementSequence } });
            }
        }
        frame = GetActiveProgramFrame(id); receipt = frame.Luanfeng!;
        if (DrainEquipmentSlotNamedChildren(frame.Id)) return true;
        if (receipt.VictimSeat == frame.OwnerSeat) AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedNamesResetEvent(frame.OwnerSeat, frame.Id));
        ReplaceRuntimeTop(frame with { Luanfeng = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(frame.Id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 26101)]
    private bool ReturnLuanfengMovement(ProgramSkillFrame frame) => frame.Luanfeng is not null && ResumeLuanfeng(frame.Id);
}
