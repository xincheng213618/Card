namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 余威 is active while its play-phase evidence exists for the running turn:
    // the trigger only commits on other living Qun-faction characters' turns.
    private bool IsYuWeiLimitDoubled(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ProgramYuWeiActiveTurnEvent>()
            .Any(e => e.OwnerSeat == ownerSeat && e.TurnNumber == _turnNumber);

    // 余威: commit the per-turn evidence consumed by 诗怨's limit query.
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
