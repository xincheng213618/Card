namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static readonly CardMoveReason SimultaneousTopReservationReason = new("skill.pindian.reserve-top");
    [ProgramViewContributor(ProgramViewContribution.ConcealedCardExclusion, 10)]
    private bool IsSimultaneousPrivateTopCard(int id) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SimultaneousPindian is { Revealed: false } r && r.TopReceipts.Any(t => t.CardId == id));
    private CardLocation SimultaneousOriginalLocation(SimultaneousPindianReceipt r,PindianRevealedCard card) =>
        r.TopReceipts.Any(t => t.Seat == card.Seat && t.CardId == card.CardId) ? CardLocation.Processing : CardLocation.Hand(card.Seat);
    private bool CanOfferSimultaneousTop(ProgramSkillFrame f,int seat) => CanOfferPindianTopChoice(_players[seat]);
    private void ContinueSimultaneousRandomPolicies(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!; var selection = r.RandomSelection!;
        while (selection.Index < selection.Candidates.Count)
        {
            var c = selection.Candidates[selection.Index];
            // A simultaneously shared source entity can be forced only once.
            // Other participants keep independent exact target receipts.
            if (selection.Receipts.All(p => p.TargetSeat != c.TargetSeat) &&
                PindianPolicySourceLive(c.OwnerSeat,c.Source,SkillProgramCardPolicyKind.PindianOpponentRandomHand) && GetHand(_players[c.TargetSeat]).Count > 0)
            { ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { RandomSelection = selection } }); PublishSimultaneousChoice(f); return; }
            selection = selection with { Index = selection.Index + 1 };
        }
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { RandomSelection = selection, Stage = SimultaneousPindianStage.Selecting } });
        ContinueSimultaneousSelection(f);
    }
    private void ResolveSimultaneousRandomPolicy(ProgramSkillFrame f,PromptChoice choice)
    {
        var r = f.SimultaneousPindian!; var selection = r.RandomSelection!; var c = selection.Candidates[selection.Index];
        if (selection.Receipts.Any(p => p.TargetSeat == c.TargetSeat) || !PindianPolicySourceLive(c.OwnerSeat,c.Source,SkillProgramCardPolicyKind.PindianOpponentRandomHand))
            throw new InvalidOperationException("A simultaneous random policy lost its exact live owner or already-fixed target.");
        if (choice.Parameters["branch"] == "random")
        {
            var hand = GetHand(_players[c.TargetSeat]).OrderBy(card => card.Id).ToArray(); var before = _random.State;
            var card = hand[_random.Next(hand.Length)]; var paid = new PindianRandomReceipt(c.OwnerSeat,c.TargetSeat,c.Source,card.Id,before,_random.State);
            selection = selection with { Receipts = Array.AsReadOnly(selection.Receipts.Append(paid).ToArray()), Index = selection.Index + 1 };
            if (c.TargetSeat == f.OwnerSeat)
            {
                var rank = EffectivePindianRank(_players[c.TargetSeat],card);
                r = r with { Cards = [new(c.TargetSeat,card.Id,rank,rank)] };
            }
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { RandomSelection = selection } });
            AdvanceEventRulesAndQueueFact(new PindianRandomHandCommittedEvent(f.Id,c.OwnerSeat,c.TargetSeat,c.Source));
        }
        if (choice.Parameters["branch"] != "random") selection = selection with { Index = selection.Index + 1 };
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = f.SimultaneousPindian! with { RandomSelection = selection } });
        ContinueSimultaneousRandomPolicies(f);
    }
    private void ContinueSimultaneousSelection(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!;
        // Every reserved top is already privately held in Processing, so an
        // exhausted deck can refill normally for the next participant.
        if (r.SelectionIndex < r.Opponents.Count && _cardZones.Count(CardLocation.DrawPile) == 0 &&
            _cardZones.Count(CardLocation.DiscardPile) > 0 && CardPolicies(_players[r.Opponents[r.SelectionIndex]],SkillProgramCardPolicyKind.PindianTopCardChoice).Any() &&
            !r.RandomSelection!.Receipts.Any(p => p.TargetSeat == r.Opponents[r.SelectionIndex]))
        {
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Stage = SimultaneousPindianStage.TopRefillChildren, PaymentKind = "top-refill", MovementBefore = before, MovementAfter = before }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
            EnsureDrawPile(); f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { SimultaneousPindian = f.SimultaneousPindian! with { MovementAfter = _movementSequence } });
            if (!DrainSimultaneousChildren(f)) AdvanceRuntimeProgram(f.Id); return;
        }
        while (r.SelectionIndex < r.Opponents.Count)
        {
            var seat = r.Opponents[r.SelectionIndex]; var forced = r.RandomSelection!.Receipts.SingleOrDefault(p => p.TargetSeat == seat);
            if (forced is null) { PublishSimultaneousChoice(f); return; }
            var card = GetHand(_players[seat]).Single(c => c.Id == forced.CardId); var rank = EffectivePindianRank(_players[seat],card);
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { Cards = r.Cards.Append(new(seat,card.Id,rank,rank)).ToArray(), SelectionIndex = r.SelectionIndex + 1 } });
            ContinueSimultaneousSelection(f); return;
        }
        RevealSimultaneousCommittedGroup(f);
    }
    private void CommitSimultaneousSelection(ProgramSkillFrame f,int seat,PromptChoice choice)
    {
        var r = f.SimultaneousPindian!; Card card; int rank;
        if (choice.Parameters["branch"] == "maximum")
        {
            if (!CanWuLuKangDiscardForPindian(_players[seat])) throw new InvalidOperationException("The discard-backed source lost its actual HE policy.");
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Cards = r.Cards.Append(new(seat, -seat - 1, 0, 0)).ToArray(), SelectionIndex = r.SelectionIndex + 1 } }); ContinueSimultaneousSelection(f); return;
        }
        if (choice.Parameters["branch"] == "top")
        {
            if (!CanOfferSimultaneousTop(f,seat) || r.RandomSelection!.Index != r.RandomSelection.Candidates.Count || r.RandomSelection.Receipts.Any(p => p.TargetSeat == seat) ||
                f.PendingMovementContinuation is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
                throw new InvalidOperationException("A simultaneous top requires its exact unforced participant after every paid policy child.");
            card = _cardZones.CardsAt(CardLocation.DrawPile).Last();
            var binding = CardPolicies(_players[seat],SkillProgramCardPolicyKind.PindianTopCardChoice).First();
            var source = new CardConversionSource(binding.Source.SkillId,binding.Policy.Id,seat,binding.Source.SkillInstanceId);
            rank = card.Kind == CardKind.Alcohol && HasAlcoholKingIdentityPolicy(_players[seat]) ? 13 : EffectivePindianRank(_players[seat],card);
            var before = _movementSequence;
            r = r with { TopReceipts = r.TopReceipts.Append(new(seat,card.Id,rank,source,before + 1)).ToArray(),
                Cards = r.Cards.Append(new(seat,card.Id,rank,rank)).ToArray(), SelectionIndex = r.SelectionIndex + 1,
                Stage = SimultaneousPindianStage.TopRefillChildren, PaymentKind = "top-reserve", MovementBefore = before, MovementAfter = before + 1 };
            ReplaceRuntimeTop(f = f with { SimultaneousPindian = r, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
            MoveCard(card,CardLocation.DrawPile,CardLocation.Processing,SimultaneousTopReservationReason,
                _ => AdvanceEventRulesAndQueueFact(new SimultaneousPindianTopCommittedEvent(f.Id,seat,source)),emitCardMovedEvents:false);
            if (!DrainSimultaneousChildren(f)) AdvanceRuntimeProgram(f.Id);
            return;
        }
        else { card = GetHand(_players[seat]).Single(c => c.Id == choice.Cards.Single()); rank = EffectivePindianRank(_players[seat],card); }
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Cards = r.Cards.Append(new(seat,card.Id,rank,rank)).ToArray(), SelectionIndex = r.SelectionIndex + 1 } });
        ContinueSimultaneousSelection(f);
    }
    private void RevealSimultaneousCommittedGroup(ProgramSkillFrame f)
    {
        if (TryBeginWuLuKangGroupBeforeReveal(f)) return;
        if (TryBeginDouyuSimultaneousPreReveal(f)) return;
        var r = f.SimultaneousPindian!;
        var inputs = r.Cards.Select(c => WuLuKangActualInput(f, c.Seat, c.CardId, SimultaneousOriginalLocation(r,c), c.OriginalRank, r.TopReceipts.Any(t => t.CardId == c.CardId))).ToArray();
        if (inputs.Any(c => c.CardId <= 0 || _cardZones.GetLocation(c.CardId) != c.From))
            throw new InvalidOperationException("A simultaneous private commitment lost its original or exactly paid replacement entity.");
        var before = _movementSequence;
        foreach (var c in inputs.Where(c => c.From != CardLocation.Processing)) MoveCard(GetAdvancedCard(c.CardId),c.From,CardLocation.Processing,CardMoveReasons.PindianReveal);
        RevealWuLuKangInputs(f); f = GetActiveProgramFrame(f.Id);
        var mappedCards = ApplyDouyuSimultaneousCards(f, inputs.Select(c => new PindianRevealedCard(c.Seat, c.CardId, c.Rank, c.Rank)).ToArray());
        f = GetActiveProgramFrame(f.Id); r = f.SimultaneousPindian!;
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r = r with { Cards = mappedCards, Stage = SimultaneousPindianStage.RevealChildren, Revealed = true,
            RevealBefore = before, RevealAfter = _movementSequence, MovementBefore = before, MovementAfter = _movementSequence, PaymentKind = "reveal" }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        foreach (var t in r.TopReceipts) AdvanceEventRulesAndQueueFact(new PindianTopSourceChosenEvent(t.Seat,t.Source,t.CardId));
        AdvanceEventRulesAndQueueFact(new PindianCardsRevealedEvent(f.Id,r.Cards,true));
        if (!DrainSimultaneousChildren(f)) AdvanceRuntimeProgram(f.Id);
    }
    private void AssertSimultaneousSelectionPolicies(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!; var selection = r.RandomSelection;
        var originalCards = WuLuKangOriginalSelectedGroup(f);
        if (selection is null || selection.Candidates is not System.Collections.ObjectModel.ReadOnlyCollection<PindianRandomCandidate> ||
            selection.Receipts is not System.Collections.ObjectModel.ReadOnlyCollection<PindianRandomReceipt> || selection.Index < 0 || selection.Index > selection.Candidates.Count ||
            selection.Receipts.GroupBy(p => p.TargetSeat).Any(p => p.Count() != 1) || selection.Candidates.Any(c => c.OwnerSeat == c.TargetSeat ||
                c.Source.OwnerSeat != c.OwnerSeat || c.OwnerSeat != f.OwnerSeat && !r.Opponents.Contains(c.OwnerSeat) || c.TargetSeat != f.OwnerSeat && !r.Opponents.Contains(c.TargetSeat)) ||
            selection.Receipts.Any(p => p.RandomBefore == p.RandomAfter || !selection.Candidates.Take(selection.Index).Any(c => c.OwnerSeat == p.OwnerSeat && c.TargetSeat == p.TargetSeat && c.Source == p.Source) ||
                !r.Revealed && !IsWuLuKangPrivateGroup(f) && _cardZones.GetLocation(p.CardId) != CardLocation.Hand(p.TargetSeat) ||
                CompleteProgramEventHistory().OfType<PindianRandomHandCommittedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == p.OwnerSeat && e.TargetSeat == p.TargetSeat && e.Source == p.Source) != 1))
            throw new InvalidOperationException("A simultaneous random choice lost its private once-target entity, policy source or RNG receipt.");
        var actualSource = selection.Receipts.SingleOrDefault(p => p.TargetSeat == f.OwnerSeat)?.CardId ?? r.OriginalSourceCardId;
        if (actualSource != originalCards[0].CardId || r.TopReceipts.Select(t => t.Seat).Distinct().Count() != r.TopReceipts.Count ||
            r.TopReceipts.Select(t => t.CardId).Distinct().Count() != r.TopReceipts.Count || r.TopReceipts.Any(t => t.Source.OwnerSeat != t.Seat ||
                !originalCards.Any(c => c.Seat == t.Seat && c.CardId == t.CardId && c.OriginalRank == t.EffectiveRank) ||
                _cardMovements.Count(m => m.Sequence == t.ReservationMovementSequence && m.CardId == t.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason == SimultaneousTopReservationReason) != 1 ||
                !r.Revealed && _cardZones.GetLocation(t.CardId) != CardLocation.Processing ||
                CompleteProgramEventHistory().OfType<SimultaneousPindianTopCommittedEvent>().Count(e => e.ProgramFrameId == f.Id && e.ParticipantSeat == t.Seat && e.Source == t.Source) != 1))
            throw new InvalidOperationException("A simultaneous source input/top reservation lost its distinct original invoice or hidden exclusive entity.");
    }
}
