namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardUseFrame? NingwuEligibleUse(int holder, ProgramSkillWindowContext context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } identity } || LifecycleCardUse(identity.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } use || action.ActionId != identity.CardActionId || action.ActorSeat == holder || !IsSlashCard(action.EffectiveKind) || use.SourceSeat != action.ActorSeat || action.EffectiveDesignatedTargetSeats is not [var target] || !_players[target].IsAlive || ZhouQunNeighbors(action.ActorSeat).Contains(target)) return null;
        return use;
    }
}
