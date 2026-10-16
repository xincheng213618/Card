namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksPublicHandReveals => _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveDailiTurnEnd) == true;
    private long? CurrentHandEntry(int owner, int id)
    {
        if (_cardZones.GetLocation(id) != CardLocation.Hand(owner)) return null;
        for (var index = _cardMovements.Count - 1; index >= 0; index--)
        {
            var movement = _cardMovements[index];
            if (movement.CardId == id && movement.From != movement.To)
                return movement.To == CardLocation.Hand(owner) ? movement.Sequence : null;
        }
        return 0;
    }
    private IEnumerable<PublicHandEntityRevealedEvent> CurrentPublicHandReveals() => !TracksPublicHandReveals ? [] :
        CompleteProgramEventHistory().OfType<PublicHandEntityRevealedEvent>()
            .GroupBy(e => (e.OwnerSeat, e.Card.Id, e.HandEntryMovementSequence)).Select(g => g.Last())
            .Where(e => CurrentHandEntry(e.OwnerSeat, e.Card.Id) == e.HandEntryMovementSequence);
    private IReadOnlyList<CardSnapshot> GetPubliclyRevealedHandCards() =>
        Array.AsReadOnly(CurrentPublicHandReveals().Select(e => e.Card with { PublicHandOwnerSeat = e.OwnerSeat }).OrderBy(c => c.Id).ToArray());
    private int PubliclyRevealedHandCount(int owner) => CurrentPublicHandReveals().Count(e => e.OwnerSeat == owner);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 26420)]
    private void ObserveExplicitPublicHandReveal(IGameEvent payload)
    {
        if (!TracksPublicHandReveals) return;
        var (frame, ids) = payload switch
        {
            ProgramCardsRevealedEvent e => (e.FrameId, e.Cards.Select(c => c.Id)),
            CardsRevealedEvent e => (e.ResolutionId, e.Cards.Select(c => c.Id)),
            FireAttackCardRevealedEvent e => (e.ResolutionId, new[] { e.CardId }.AsEnumerable()),
            ProgramHandComparisonSourceRevealedEvent e => (e.FrameId, new[] { e.CardId }.AsEnumerable()),
            ProgramHandComparisonCardRevealedEvent e => (e.FrameId, new[] { e.CardId }.AsEnumerable()),
            _ => (0L, Enumerable.Empty<int>())
        };
        foreach (var id in ids.Distinct().ToArray())
        {
            var location = _cardZones.GetLocation(id);
            if (location is not { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } || CurrentHandEntry(owner, id) is not { } entry) continue;
            if (CompleteProgramEventHistory().OfType<PublicHandEntityRevealedEvent>().Any(e => e.OwnerSeat == owner && e.Card.Id == id && e.HandEntryMovementSequence == entry)) continue;
            AdvanceEventRulesAndQueueFact(new PublicHandEntityRevealedEvent(frame, owner, entry, ToSnapshot(GetAdvancedCard(id))));
        }
    }
}
