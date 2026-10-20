using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MouYangweiCooling(int owner) => ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(e => e.OwnerSeat == owner &&
        !ProgramEventHistory<MouHuaXiongCooldownRecoveredEvent>().Any(returned => returned.FrameId == e.FrameId));
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 29100, SkillProgramEffectOp.ResolveMouHuaXiongYangwei)]
    private bool CanActivateMouYangwei(ProgramActivationLegalityContext c) => c.SkillId == MouYangwei && MouHuaXiongComposition.IsYangwei(c.Program) &&
        _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat && _turnNumber > 0 && c.Owner.IsAlive && !MouYangweiCooling(c.OwnerSeat);
    private bool MouYangweiRoot(ProgramSkillFrame f) => f.SkillId == MouYangwei && f.ActivationId == "draw-and-wei" && f.TriggerId is null &&
        f.WindowContext is null && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        _contentRegistry.GetSkill(f.SkillId).Program is { } p && p.GameplayHash == f.GameplayHash && MouHuaXiongComposition.IsYangwei(p);
    private SkillProgramStepOutcome ResolveMouYangwei(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!MouYangweiRoot(f)) throw new InvalidOperationException("Yangwei needs its exact complete zero-card Play activation.");
        if (f.InstructionIndex == 1)
        {
            if (f.MouHuaXiongYangwei is not null || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || _turnNumber < 1 ||
                !_players[f.OwnerSeat].IsAlive || MouYangweiCooling(f.OwnerSeat) ||
                ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(e => e.FrameId == f.Id))
                throw new InvalidOperationException("Yangwei cannot repeat an issued phase activation or bypass its owner-turn cooldown.");
            var receipt = new MouHuaXiongYangweiReceipt(MouHuaXiongSource(f), f.GameplayHash, _turnNumber, _currentSeat, _cardUseDebitPhaseInstanceId);
            ReplaceRuntimeTop(f with { MouHuaXiongYangwei = receipt });
            AdvanceEventRulesAndQueueFact(new MouHuaXiongYangweiReservedEvent(f.Id, f.OwnerSeat, receipt.ActualTurn, receipt.PlayPhaseId, receipt.Source, receipt.GameplayHash));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.InstructionIndex != 3 || f.MouHuaXiongYangwei is not { WeiResolved: false } r || !ValidMouYangweiReceipt(f) || f.NativeDraw is not null ||
            ProgramEventHistory<MouHuaXiongWeiGrantedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Yangwei's Wei grant must follow its once-paid Draw and all native children.");
        var expired = !_players[f.OwnerSeat].IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || _currentSeat != r.ActualOwner ||
            _turnNumber != r.ActualTurn || _cardUseDebitPhaseInstanceId != r.PlayPhaseId;
        ReplaceRuntimeTop(f with { MouHuaXiongYangwei = r with { WeiResolved = true } });
        AdvanceEventRulesAndQueueFact(new MouHuaXiongWeiGrantedEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.PlayPhaseId, expired));
        return SkillProgramStepOutcome.Continue;
    }
    private bool ValidMouYangweiReceipt(ProgramSkillFrame f) => MouYangweiRoot(f) && f.MouHuaXiongYangwei is { } r &&
        r.Source == MouHuaXiongSource(f) && r.GameplayHash == f.GameplayHash && r.ActualOwner == f.OwnerSeat && r.ActualTurn > 0 && r.PlayPhaseId > 0 &&
        ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Count(e => e == new MouHuaXiongYangweiReservedEvent(f.Id, f.OwnerSeat,
            r.ActualTurn, r.PlayPhaseId, r.Source, r.GameplayHash)) == 1;
    // A paid public mark survives loss of the granting source. It is not an
    // attack-range increase and never substitutes for native target prohibitions.
    private bool HasMouHuaXiongWei(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive && _phase == TurnPhase.Play &&
        _currentSeat == owner && ProgramEventHistory<MouHuaXiongWeiGrantedEvent>().Any(e => e.OwnerSeat == owner && !e.ExpiredBeforeGrant &&
            e.ActualTurn == _turnNumber && e.PlayPhaseId == _cardUseDebitPhaseInstanceId &&
            ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Any(r => r.FrameId == e.FrameId && r.OwnerSeat == owner &&
                r.ActualTurn == e.ActualTurn && r.PlayPhaseId == e.PlayPhaseId));
    private IReadOnlyList<PlayerMarkerSnapshot>? AppendMouHuaXiongWei(int owner, IReadOnlyList<PlayerMarkerSnapshot>? ordinary) => HasMouHuaXiongWei(owner)
        ? Array.AsReadOnly((ordinary ?? []).Where(m => m.Kind != PlayerMarkerKind.MouHuaXiongWei)
            .Append(new PlayerMarkerSnapshot(PlayerMarkerKind.MouHuaXiongWei, "威", 1)).OrderBy(m => m.Kind).ToArray()) : ordinary;
    // Invoked at the actual native Ending entry, including an owner's extra
    // turn. A face-down whole-turn skip creates no Ending and does not recover.
    private void RecoverMouYangweiAtEnding(int owner)
    {
        if (_turnNumber < 1 || _currentSeat != owner || _turnProgression.OwnerSeat != owner || _turnProgression.TurnNumber != _turnNumber)
            throw new InvalidOperationException("Yangwei cooldown recovery requires the actual turn's native Ending entry.");
        foreach (var issued in ProgramEventHistory<MouHuaXiongYangweiReservedEvent>().Where(e => e.OwnerSeat == owner && e.ActualTurn < _turnNumber &&
            !ProgramEventHistory<MouHuaXiongCooldownRecoveredEvent>().Any(returned => returned.FrameId == e.FrameId)).ToArray())
            AdvanceEventRulesAndQueueFact(new MouHuaXiongCooldownRecoveredEvent(issued.FrameId, owner, issued.ActualTurn, _turnNumber));
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 20)]
    private bool DistanceCardUseExemption20HasMouHuaXiongWei(ProgramCardDistanceQuery q) =>
        IsSlashCard(q.Kind) && HasMouHuaXiongWei(q.Actor.Seat);

}
