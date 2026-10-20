namespace CardGame.Core;

public sealed partial class GameEngine
{

    private static bool IsBianFuRenWanweiTrigger(SkillProgramTrigger trigger) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_bian_fu_ren_ol_Wanwei_Qualifications.IsBianFuRenWanweiTrigger(trigger);

    private bool HasBianFuRenWanwei(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
        GetSkillBindingShard(_players[owner]).GetInstanceTriggers(SkillProgramTriggerWindow.CardsMoved)
            .Any(binding => IsBianFuRenWanweiTrigger(binding.Trigger));
}
