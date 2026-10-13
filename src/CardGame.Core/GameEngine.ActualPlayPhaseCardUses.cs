namespace CardGame.Core;

/// <summary>Immutable content opt-in, independent of any general identity.</summary>
internal interface IActualPlayPhaseUseLedgerOperation { }
public sealed record ActualPlayPhaseCardUseState(int ActorSeat, int TurnNumber, int PhaseInstanceId, int UseCount);
public sealed record ActualPlayPhaseCardUseRecordedEvent(ActualPlayPhaseCardUseState State, long CardActionId) : IGameEvent
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardKind? EffectiveCardKind { get; init; }
}

public sealed partial class GameEngine
{
    private readonly HashSet<long> _actualPlayPhaseUseActions = [];
    private bool TracksActualPlayPhaseCardUses => _contentRegistry.ProgramDependencies.UsesActualPlayPhaseUseLedger;

    private int GetActualPlayPhaseUseCount(int actorSeat) =>
        _phase == TurnPhase.Play && actorSeat == _currentSeat ? _actualPlayPhaseUseActions.Count : 0;

    // Only genuine own-phase uses reach this hook. Typed response callers have
    // already distinguished own Slash-defense Dodge/Nullification from played
    // responses and supplied faction cards, after their actual entity payment.
    private void RecordActualPlayPhaseUse(CardActionContext action)
    {
        ObservePhaseNamePredictionUse(action, qualifiedActualUse: true);
        RecordOwnPlayEligibleUse(action);
        RecordFirstTurnCategoryUse(action);
        if (!TracksActualPlayPhaseCardUses || _phase != TurnPhase.Play || action.ActorSeat != _currentSeat) return;
        if (_actualPlayPhaseUseActions.Add(action.ActionId))
            AdvanceEventRulesAndQueueFact(new ActualPlayPhaseCardUseRecordedEvent(
                new(action.ActorSeat, _turnNumber, _cardUseDebitPhaseInstanceId, _actualPlayPhaseUseActions.Count), action.ActionId)
            { EffectiveCardKind = _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.StopUsingAfterTrickOrHpCount)
                ? action.EffectiveKind : null });
    }
}
