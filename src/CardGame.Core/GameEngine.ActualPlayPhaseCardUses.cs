namespace CardGame.Core;

/// <summary>Immutable content opt-in, independent of any general identity.</summary>
internal interface IActualPlayPhaseUseLedgerOperation { }
public sealed record ActualPlayPhaseCardUseState(int ActorSeat, int TurnNumber, int PhaseInstanceId, int UseCount);
public sealed record ActualPlayPhaseCardUseRecordedEvent(ActualPlayPhaseCardUseState State, long CardActionId) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly HashSet<long> _actualPlayPhaseUseActions = [];
    private bool? _tracksActualPlayPhaseCardUses;
    private bool TracksActualPlayPhaseCardUses => _tracksActualPlayPhaseCardUses ??=
        _contentRegistry.Skills.Values.Any(skill => skill.Program is { } program &&
            (program.CardPolicies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.FirstActualPlayUseDistanceUnlimited) ||
             program.Triggers.SelectMany(trigger => trigger.Effects)
                 .Concat(program.Activations.SelectMany(activation => activation.Effects))
                 .Any(effect => ProgramOperationCatalog.Default.Resolve(effect.Op) is IActualPlayPhaseUseLedgerOperation)));

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
                new(action.ActorSeat, _turnNumber, _cardUseDebitPhaseInstanceId, _actualPlayPhaseUseActions.Count), action.ActionId));
    }
}
