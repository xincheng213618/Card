namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 毒逝: the chosen living character acquires 毒逝 with both of its clauses.
    private SkillProgramStepOutcome DuShiProgramGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var recipient] || !IsValidPlayerSeat(recipient) ||
            recipient == active.OwnerSeat || !_players[recipient].IsAlive || _winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "毒逝的继承角色已失效，剩余结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var recipientState = _players[recipient];
        AcquireRuntimeSkills(recipientState, active.SkillId, [active.SkillId]);
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【{_contentRegistry!.GetSkill(active.SkillId).Name}】由 {recipientState.Name} 获得。",
            active.OwnerSeat, recipient);
        AdvanceEventRulesAndQueueFact(new ProgramDuShiGrantEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, recipient));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }
}
