namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 旋回 disables from its own swap until any character dies afterwards;
    // multiple owners disable only their own instance.
    private bool IsXuanhuiDisabledByOwnSwap(int ownerSeat)
    {
        if (!ProgramEventHistory<ProgramXuanhuiEffectsSwappedEvent>().Any(item => item.OwnerSeat == ownerSeat))
            return false;
        var facts = CompleteProgramEventHistory().ToArray();
        var swapIndex = Array.FindLastIndex(facts, item =>
            item is ProgramXuanhuiEffectsSwappedEvent swapped && swapped.OwnerSeat == ownerSeat);
        if (swapIndex < 0) return false;
        var deathIndex = Array.FindLastIndex(facts, item => item is PlayerDiedEvent);
        return deathIndex < swapIndex;
    }

    internal bool IsFengjiSwapAvailable(int ownerSeat)
    {
        if (_winner != Winner.None || IsXuanhuiDisabledByOwnSwap(ownerSeat)) return false;
        return CurrentRoundFengjiOptions(ownerSeat).Any(option => option.Accepted &&
            option.RecipientSeat != ownerSeat && _players[option.RecipientSeat].IsAlive);
    }

    // 旋回: the owner and every current 丰积 beneficiary exchange the matching
    // accepted-option effects for the rest of the round; the skill then
    // disables until a character dies.
    private SkillProgramStepOutcome XuanhuiProgramSwapEffects(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _roundNumber == 0 ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            IsXuanhuiDisabledByOwnSwap(active.OwnerSeat))
            return SkillProgramStepOutcome.Continue;
        var swapped = CurrentRoundFengjiOptions(active.OwnerSeat).Where(option => option.Accepted &&
            option.RecipientSeat != active.OwnerSeat).ToArray();
        if (swapped.Length == 0) return SkillProgramStepOutcome.Continue;
        AdvanceEventRulesAndQueueFact(new ProgramXuanhuiEffectsSwappedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, _roundNumber, swapped.Length));
        AddLog("SkillEffect",
            $"{owner.Name} 旋回：与 {_players[swapped[0].RecipientSeat].Name} 等 {swapped.Length} 项丰积效果交换，【旋回】失效直到一名角色死亡。",
            active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }
}
