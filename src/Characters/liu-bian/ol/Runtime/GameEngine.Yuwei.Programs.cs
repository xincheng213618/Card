namespace CardGame.Core;

public sealed partial class GameEngine
{

    // The limit applies throughout the actual Qun turn, including native uses
    // before Play. The qualified binding shard also observes Lord qualification,
    // loss and suppression; an old Play-phase fact cannot keep a lost source alive.
    private bool IsYuWeiLimitDoubled(int ownerSeat) =>
        _turnNumber > 0 && IsValidPlayerSeat(ownerSeat) && IsValidPlayerSeat(_currentSeat) &&
        ownerSeat != _currentSeat && _players[ownerSeat].IsAlive &&
        _turnProgression.TurnNumber == _turnNumber && _turnProgression.OwnerSeat == _currentSeat &&
        GetEffectiveFactionId(_players[_currentSeat]) == "qun" &&
        GetSkillBindingShard(_players[ownerSeat]).ProgramInstances.Any(instance => instance.Program.Triggers
            .Any(trigger => ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.YuWeiMarkActiveTurn)));

    // Retain the ordinary Play-phase notification and its historical event shape.
    private SkillProgramStepOutcome YuWeiProgramMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        AddLog("SkillEffect",
            $"{owner.Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】生效：其他群势力角色的回合内，诗怨改为每回合每项限两次。",
            owner.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramYuWeiActiveTurnEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }
}
