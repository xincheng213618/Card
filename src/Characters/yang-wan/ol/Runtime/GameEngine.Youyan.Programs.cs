namespace CardGame.Core;

public sealed partial class GameEngine
{

    // 诱言: the whole discard batch's own-discarded suits stay out of the gain;
    // every other suit is taken once from the top of the draw pile.
    private SkillProgramStepOutcome YouyanProgramGainSuitCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch } ||
            _winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var discardedSuitMask = ProgramYouyanDiscardedSuitMask(owner.Seat, batch);
        if (discardedSuitMask == 0) return SkillProgramStepOutcome.Continue;
        var gained = 0;
        foreach (var suit in new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond })
        {
            if ((discardedSuitMask & SuitMask(suit)) != 0) continue;
            var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(item => item.Suit == suit);
            if (card is null) continue;
            MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(owner.Seat),
                new($"skill-program.{active.SkillId}.youyan-gain"));
            gained++;
        }
        if (gained == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 诱言：牌堆中没有其余花色的牌可获得。", active.OwnerSeat);
            return SkillProgramStepOutcome.Continue;
        }
        AddLog("SkillEffect",
            $"{owner.Name} 诱言：从牌堆中获得 {gained} 张与弃置牌花色不同的牌。",
            active.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new ProgramYouyanGainEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, discardedSuitMask, gained));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private int ProgramYouyanDiscardedSuitMask(int ownerSeat, CardMovementBatchContext batch)
    {
        var mask = 0;
        foreach (var movement in batch.Movements)
        {
            if (movement.To != CardLocation.DiscardPile ||
                GetProgramDiscardSource(movement) is not { OwnerSeat: var source } || source != ownerSeat)
                continue;
            var card = _cardZones.CardsAt(_cardZones.GetLocation(movement.CardId))
                .SingleOrDefault(item => item.Id == movement.CardId);
            if (card is not null) mask |= SuitMask(card.Suit);
        }
        return mask;
    }
}
