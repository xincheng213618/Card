namespace CardGame.Core;

public sealed partial class GameEngine
{

    private static bool IsBianFuRenWanweiTrigger(SkillProgramTrigger trigger) => trigger.Effects
        .Any(effect => effect.Op == SkillProgramEffectOp.WanweiObtainMatchingLostCard);

    private bool HasBianFuRenWanwei(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
        GetSkillBindingShard(_players[owner]).GetInstanceTriggers(SkillProgramTriggerWindow.CardsMoved)
            .Any(binding => IsBianFuRenWanweiTrigger(binding.Trigger));
}
