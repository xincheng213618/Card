namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<CardActionCost>? CaptureZhangbaResponseCompletionCosts(CharacterState responder, IReadOnlyList<Card> pair) =>
        !_contentRegistry.ProgramDependencies.HasTriggerWindow(SkillProgramTriggerWindow.CardResponseCompleted) ? null :
        Array.AsReadOnly(pair.Select(card => new CardActionCost(card.Id, card.Kind,
            FindOwnedCardLocation(responder, card), CapturePhysicalCardColor(responder.Seat, card))).ToArray());
}
