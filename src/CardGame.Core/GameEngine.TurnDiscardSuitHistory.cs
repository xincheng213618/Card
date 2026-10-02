namespace CardGame.Core;

/// <summary>Opt-in scalar history; contains no card identities or private locations.</summary>
public sealed record TurnDiscardSuitMaskChangedEvent(int TurnNumber, int SuitMask) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksTurnDiscardSuits => _contentRegistry.ProgramDependencies.UsesTriggerCondition(SkillProgramTriggerConditionKind.TurnDiscardIncludesAllSuits);
    private int CurrentTurnDiscardSuitMask => CompleteProgramEventHistory().OfType<TurnDiscardSuitMaskChangedEvent>()
        .LastOrDefault(e => e.TurnNumber == _turnNumber)?.SuitMask ?? 0;
    private void CaptureTurnDiscardSuitFact(int turn, IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksTurnDiscardSuits || turn <= 0) return;
        if (turn != _turnNumber) throw new InvalidOperationException("A discard batch lost its actual turn.");
        var previousMask = CurrentTurnDiscardSuitMask;
        var mask = previousMask;
        foreach (var move in movements.Where(m => m.To == CardLocation.DiscardPile && m.From != CardLocation.DiscardPile))
        {
            var suit = _cardZones.CardsAt(move.To).Single(c => c.Id == move.CardId).Suit;
            mask |= suit switch { Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0 };
        }
        if (mask != previousMask) AdvanceEventRulesAndQueueFact(new TurnDiscardSuitMaskChangedEvent(turn, mask));
    }
    private int? ProgramTriggerUsageLimit(SkillProgramTrigger trigger) => trigger.DynamicUsageLimit is { Kind: SkillProgramDynamicUsageLimitKind.AlivePlayersCapped } dynamic
        ? Math.Min(dynamic.Cap, _players.Count(p => p.IsAlive)) : trigger.UsageLimit;
}
