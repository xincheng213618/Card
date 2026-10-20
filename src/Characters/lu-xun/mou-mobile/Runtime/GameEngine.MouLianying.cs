namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouLuXunViewReason = "skill-program.mou-mobile:lianying.view";
    private void BeginMouLuXunView(ProgramSkillFrame f)
    {
        var r = f.MouLuXun!; var cards = new List<int>();
        ReplaceRuntimeTop(f = f with { MouLuXun = r with { Paid = true, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        for (var i = 0; i < r.Limit; i++)
        {
            if (DrawOneToProcessing(new(MouLuXunViewReason)) is not { } card) break;
            cards.Add(card.Id);
            AdvanceEventRulesAndQueueFact(new MouLuXunTopViewedEvent(f.Id, i, card.Id, _movementSequence));
        }
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { MouLuXun = f.MouLuXun! with { Viewed = cards, After = _movementSequence } });
        ContinueMouLuXun(f.Id);
    }
    private void PayMouLuXunGift(ProgramSkillFrame f, int target)
    {
        var r = f.MouLuXun!; var id = r.Viewed[r.Cursor];
        if (!ValidMouLuXun(f) || !_players[target].IsAlive || _cardZones.GetLocation(id) != CardLocation.Processing)
            throw new InvalidOperationException("Lianying can only give this exact privately viewed original entity to a living recipient.");
        ReplaceRuntimeTop(f = f with { MouLuXun = r with { Stage = MouLuXunStage.GiftChildren, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(EntityAtCurrentLocation(id), CardLocation.Processing, CardLocation.Hand(target), new("skill-program.mou-mobile:lianying.give"),
            disclosure: CardMovementDisclosure.FaceDown);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouLuXun = f.MouLuXun! with { After = _movementSequence } });
        var move = _cardMovements.Last(m => m.CardId == id);
        AdvanceEventRulesAndQueueFact(new MouLuXunEntityMovedEvent(f.Id, r.Cursor, id, move.From, move.To, move.Sequence));
        ContinueMouLuXun(f.Id);
    }
    private void ReturnMouLuXunUnassigned(ProgramSkillFrame f)
    {
        var r = f.MouLuXun!; var cards = r.Viewed.Skip(r.Cursor).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing)
            .Select(EntityAtCurrentLocation).ToArray();
        if (cards.Length == 0) { FinishMouLuXun(f); return; }
        ReplaceRuntimeTop(f = f with { MouLuXun = r with { Stage = MouLuXunStage.ReturnChildren, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCards(cards, CardLocation.Processing, CardLocation.DrawPile, new("skill-program.mou-mobile:lianying.return"));
        _cardZones.PlaceDrawPileCardsAtTop(cards.Select(c => c.Id).ToArray());
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouLuXun = f.MouLuXun! with { After = _movementSequence } });
        ContinueMouLuXun(f.Id);
    }
    private bool IsMouLuXunPrivateEntity(int id) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.MouLuXun is { Paid: true } r && r.Viewed.Skip(r.Cursor).Contains(id) && _cardZones.GetLocation(id) == CardLocation.Processing && ValidMouLuXun(f));
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 32802)]
    private IEnumerable<CardSnapshot> MouLuXunPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.OwnerSeat == viewer && f.MouLuXun is not null).SelectMany(f => f.MouLuXun!.Viewed.Skip(f.MouLuXun.Cursor))
        .Where(IsMouLuXunPrivateEntity).Select(id => ToSnapshot(EntityAtCurrentLocation(id)));
    [ProgramViewContributor(ProgramViewContribution.PublicProcessingExclusion, 32802)]
    private bool HideMouLuXunPrivateEntity(int id) => IsMouLuXunPrivateEntity(id);
    [ProgramViewContributor(ProgramViewContribution.InvariantProcessing, 32802)]
    private IReadOnlyList<Card> RemoveMouLuXunPrivateProcessing(IReadOnlyList<Card> cards) => cards.Where(c => !IsMouLuXunPrivateEntity(c.Id)).ToArray();
}
