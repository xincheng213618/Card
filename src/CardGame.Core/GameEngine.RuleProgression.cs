namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramRuleObserverCatalog> ProgramRuleObserverRegistrations = new(() =>
        ProgramRuleObserverCatalog.Discover(RuntimeModuleMethods), true);
    private readonly ProgramRuleObservers _programRuleObservers;

    // Rule observers settle before the fact enters committed-observer output.
    // Post-fact rules retain their place after the queue boundary.
    private void AdvanceEventRulesAndQueueFact(IGameEvent payload)
    {
        _programRuleObservers.BeforeEvent(payload);
        QueueGameEvent(payload);
        _programRuleObservers.AfterEvent(payload);
    }

    // A publication request settles registered state rules before opening the
    // existing character, HP and movement windows and capturing notification.
    private void AdvanceRulesAndPublishState()
    {
        _programRuleObservers.BeforeStatePublish();
        if (!TryBeginCharacterStateProgramWindow() &&
            !TryBeginHpChangedProgramWindow())
        {
            TryBeginCardsMovedProgramWindow();
        }

        PublishState();
    }
}
