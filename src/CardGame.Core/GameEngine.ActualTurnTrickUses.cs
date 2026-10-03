namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksActualTurnTrickUses => _contentRegistry.ProgramDependencies
        .UsesTriggerValue(SkillProgramTriggerValueKind.OwnerTrickUsesThisActualTurn);

    // Use the accepted action, not a play-phase counter or material movement.
    // Counterspells are uses even though their accepted action type is Response.
    private void ObserveActualTurnTrickUse(IGameEvent payload)
    {
        if (!TracksActualTurnTrickUses) return;
        CardActionContext? action = payload switch
        {
            CardUseDeclaredEvent declared => _resolutionStack.OfType<CardUseFrame>()
                .SingleOrDefault(frame => frame.Id == declared.ResolutionId)?.Action,
            CardActionAcceptedEvent accepted when accepted.Action.Type == CardActionType.Response &&
                accepted.Action.EffectiveKind == CardKind.Nullification &&
                accepted.Action.ActorSeat == accepted.Action.ProviderSeat &&
                accepted.Action.ResponderSeat == accepted.Action.ActorSeat &&
                accepted.Action.RequesterSeat is null &&
                _resolutionStack.OfType<NullificationWindowFrame>().Any(window =>
                    _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == window.ParentFrameId)
                        ?.Action?.ActionId == accepted.Action.ParentActionId) => accepted.Action,
            _ => null
        };
        if (action is null || GetProgramCardCategory(action.EffectiveKind) != SkillProgramCardCategory.Trick ||
            CompleteProgramEventHistory().OfType<ActualTurnTrickUseRecordedEvent>()
                .Any(fact => fact.ActionId == action.ActionId)) return;
        AdvanceEventRulesAndQueueFact(new ActualTurnTrickUseRecordedEvent(
            _turnNumber, action.ActorSeat, action.ActionId, action.EffectiveKind));
    }

    private int ActualTurnTrickUseCount(int ownerSeat) =>
        CompleteProgramEventHistory().OfType<ActualTurnTrickUseRecordedEvent>()
            .Count(fact => fact.TurnNumber == _turnNumber && fact.ActorSeat == ownerSeat);
}
