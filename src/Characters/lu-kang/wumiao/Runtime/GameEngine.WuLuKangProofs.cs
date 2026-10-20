namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuLuKangContest(ResolutionFrame parent)
    {
        if (parent.WuLuKangContest is not { } r || r.ContestId != parent.Id || r.ActualTurn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat ||
            r.TurnOrdinal != WuLuKangTurnOrdinal || r.Original.Count is < 2 or > 4 || r.Original[0].Seat != r.SourceSeat ||
            r.Original.Select(c => c.Seat).Distinct().Count() != r.Original.Count || !r.Assignments.Select(a => a.Seat).SequenceEqual(r.Original.Select(c => c.Seat)) ||
            r.Assignments.Where(a => a.CardId > 0).Select(a => a.CardId).Distinct().Count() != r.Assignments.Count(a => a.CardId > 0) ||
            r.Original is not System.Collections.IList { IsReadOnly: true } || r.Assignments is not System.Collections.IList { IsReadOnly: true } ||
            r.Replacements is not System.Collections.IList { IsReadOnly: true } || r.Comparisons is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<WuLuKangContestStartedEvent>().Count(e => e == new WuLuKangContestStartedEvent(parent.Id, r.Simultaneous, r.SourceSeat, r.Original.Count, r.ActualTurn, r.TurnOwner, r.TurnOrdinal)) != 1) return false;
        if (parent is PindianFrame contestPindian ? r.Simultaneous || contestPindian.SourceSeat != r.SourceSeat || contestPindian.SourceCardId != r.Original[0].CardId || contestPindian.OpponentSeat != r.Original[1].Seat :
            parent is not ProgramSkillFrame { SimultaneousPindian: { } g } f || !r.Simultaneous || f.OwnerSeat != r.SourceSeat || !g.Opponents.SequenceEqual(r.Original.Skip(1).Select(c => c.Seat))) return false;
        var assigned = r.Original.ToDictionary(c => c.Seat, c => new WuLuKangPindianAssignment(c.Seat, c.CardId, r.SourcesReady ? r.Assignments.Single(a => a.Seat == c.Seat).Rank : c.Rank));
        foreach (var p in r.Replacements)
        {
            if (!assigned.ContainsKey(p.ParticipantSeat) || p.Source.OwnerSeat != p.ParticipantSeat || p.CostFrom.OwnerSeat != p.ParticipantSeat ||
                p.CostFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || p.CostSequence <= p.Before || p.CostSequence > p.After ||
                _cardMovements.Count(m => m.Sequence == p.CostSequence && m.CardId == p.CostCardId && m.CardKind == p.CostKind && m.From == p.CostFrom && m.To == p.CostTo && m.Reason.Value == WuLuKangDiscardReason) != 1 ||
                ProgramEventHistory<WuLuKangShenduanDiscardPaidEvent>().Count(e => e == new WuLuKangShenduanDiscardPaidEvent(p.FrameId, parent.Id, p.ParticipantSeat, p.CostCardId, p.CostKind, p.CostFrom, p.CostTo, p.CostSequence)) != 1 ||
                ProgramEventHistory<WuLuKangShenduanStartedEvent>().Count(e => e.FrameId == p.FrameId && e.ContestId == parent.Id && e.Source == p.Source && e.GameplayHash == p.GameplayHash && e.Stage == WuLuKangPindianStage.BeforeSourceReplacement) != 1) return false;
            if (p.ReservedCardId is { } id)
            {
                if (p.ReservedPrintedRank != GetAdvancedCard(id).Rank || p.ReservationSequence is not { } sequence || sequence <= p.CostSequence ||
                    _cardMovements.Count(m => m.Sequence == sequence && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == WuLuKangReserveReason) != 1 ||
                    ProgramEventHistory<WuLuKangMaximumReservedEvent>().Count(e => e == new WuLuKangMaximumReservedEvent(p.FrameId, parent.Id, p.ParticipantSeat)) != 1) return false;
                var actual = r.Assignments.Single(a => a.Seat == p.ParticipantSeat);
                // Final rank may be frozen by a native reveal modifier; the entity remains exact.
                assigned[p.ParticipantSeat] = new(p.ParticipantSeat, id, actual.Rank);
            }
            else if (p.ReservationSequence is not null || p.ReservedPrintedRank is not null) return false;
        }
        if (!r.Assignments.SequenceEqual(r.Original.Select(c => assigned[c.Seat]))) return false;
        if (r.SourcesReady != ProgramEventHistory<WuLuKangSourcesClosedEvent>().Any(e => e.ContestId == parent.Id)) return false;
        if (r.Revealed && !r.Cancelled && r.Assignments.Any(a => a.CardId <= 0 || a.Rank is < 1 or > 13 || ProgramEventHistory<WuLuKangPindianInputRevealedEvent>().Count(e =>
            e.ContestId == parent.Id && e.Seat == a.Seat && e.CardId == a.CardId && e.Rank == a.Rank && e.Replaced == r.Replacements.Any(p => p.ReservedCardId == a.CardId)) != 1)) return false;
        return r.Comparisons.All(c => ProgramEventHistory<WuLuKangComparisonSettledEvent>().Count(e => e == new WuLuKangComparisonSettledEvent(parent.Id, c.Index, c.Result)) == 1 &&
            ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == parent.Id && e.Result == c.Result) == 1) && r.Comparisons.Select(c => c.Index).Distinct().Count() == r.Comparisons.Count;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29700)]
    private void AssertWuLuKang(ProgramSkillFrame f)
    {
        if (f.WuLuKangShenduan is not null && !ValidWuLuKangShenduan(f)) throw new InvalidOperationException("Shenduan lost its exact native comparison, independent source, actual payment or typed minimum-draw invoice.");
        if (f.WuLuKangKegou is not null && !ValidWuLuKangKegou(f)) throw new InvalidOperationException("Kegou lost its actual boundary qualification, current iteration or genuine native pair/use result.");
    }
    private void AssertWuLuKangWindows()
    {
        foreach (var f in _resolutionStack.Where(f => f.WuLuKangContest is not null)) if (!ValidWuLuKangContest(f)) throw new InvalidOperationException("Wu Lu Kang contest lost its complete immutable native inputs and signed payments.");
        foreach (var w in _resolutionStack.OfType<WuLuKangPindianFrame>())
        {
            var index = _resolutionStack.FindIndex(f => f.Id == w.Id);
            if (index <= 0 || _resolutionStack[index - 1].Id != w.ParentFrameId || w.CandidateIndex < 0 || w.CandidateIndex > w.Entries.Count || w.Entries is not System.Collections.IList { IsReadOnly: true } ||
                ProgramEventHistory<WuLuKangWindowOpenedEvent>().Count(e => e == new WuLuKangWindowOpenedEvent(w.Id, w.ParentFrameId, w.Stage, w.ComparisonIndex, w.Entries.Count)) != 1 ||
                w.Entries.Where((e, i) => ProgramEventHistory<WuLuKangWindowCandidateEvent>().Count(fact => fact == new WuLuKangWindowCandidateEvent(w.Id, i, WuLuKangSource(e.Candidate), e.Candidate.GameplayHash, e.ParticipantSeat)) != 1).Any() ||
                w.Entries.Any(e => e.Candidate.BindingId != "native-pindian" || _contentRegistry.GetSkill(e.Candidate.SkillId).Program is not { } p || p.GameplayHash != e.Candidate.GameplayHash || !WuLuKangComposition.IsShenduan(p))) throw new InvalidOperationException("Shenduan's private/result window escaped its exact native ancestor and frozen source queue.");
        }
    }
    private bool WuLuKangPindianEdge(ResolutionFrame child, ResolutionFrame parent) => child is WuLuKangPindianFrame w ? w.ParentFrameId == parent.Id && parent.WuLuKangContest is not null && ValidWuLuKangContest(parent) :
        parent is WuLuKangPindianFrame window && child is ProgramSkillFrame f && f.WuLuKangShenduan is { } receipt && receipt.WindowId == window.Id && ValidWuLuKangShenduan(f);
    private bool IsWuLuKangPairSubtree(PindianFrame pair)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == pair.Id);
        return index >= 0 && pair.WuLuKangContest is not null && ValidWuLuKangContest(pair) && index + 1 < _resolutionStack.Count && WuLuKangPindianEdge(_resolutionStack[index + 1], pair) &&
            HasObserverSuffix(index, static (g, n) => g.WuLuKangPindianEdge(g._resolutionStack[n], g._resolutionStack[n - 1]) || g.WuLuKangObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    }
    private bool IsWuLuKangAssignedPair(PindianFrame f, PindianResult result) => f.WuLuKangContest is { SourcesReady: true, Revealed: true } r && ValidWuLuKangContest(f) &&
        result.SourceSeat == r.SourceSeat && result.OpponentSeat == r.Original[1].Seat && (f.DouyuContest is not null ? IsDouyuPairAssignment(f, result) :
        result.SourceCardId == r.Assignments[0].CardId && result.OpponentCardId == r.Assignments[1].CardId);
    private IReadOnlyList<PindianRevealedCard> WuLuKangOriginalSelectedGroup(ProgramSkillFrame f) => f.WuLuKangContest is { } r ? r.Original.Select(c => new PindianRevealedCard(c.Seat, c.CardId, c.Rank, c.Rank)).ToArray() : GetDouyuOriginalSimultaneousCards(f);
    private bool IsWuLuKangPrivateGroup(ProgramSkillFrame f) => f.WuLuKangContest is { Revealed: false } && ValidWuLuKangContest(f) && _resolutionStack.Any(w => w is WuLuKangPindianFrame window && window.ParentFrameId == f.Id);
    private bool ValidWuLuKangGroupReveal(ProgramSkillFrame f)
    {
        if (f.WuLuKangContest is not { SourcesReady: true, Revealed: true, Cancelled: false } r || f.SimultaneousPindian is not { Revealed: true } g || !ValidWuLuKangContest(f)) return false;
        foreach (var a in r.Assignments)
        {
            var input = WuLuKangActualInput(f, a.Seat, a.CardId, CardLocation.Hand(a.Seat), a.Rank, false);
            if (input.From == CardLocation.Processing)
            {
                if (input.ReservationSequence is not { } sequence || _cardMovements.Count(m => m.Sequence == sequence && m.CardId == input.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) != 1) return false;
            }
            else if (_cardMovements.Count(m => m.Sequence > g.RevealBefore && m.Sequence <= g.RevealAfter && m.CardId == input.CardId && m.From == input.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal) != 1) return false;
        }
        return ProgramEventHistory<PindianCardsRevealedEvent>().Count(e => e.ParentFrameId == f.Id && e.Simultaneous && e.Cards.Select(c => (c.Seat, c.CardId, c.OriginalRank)).SequenceEqual(g.Cards.Select(c => (c.Seat, c.CardId, c.OriginalRank)))) == 1;
    }
    private bool IsWuLuKangHistoricalPairAssignment(long contest, PindianResult result, int original) => ProgramEventHistory<WuLuKangContestStartedEvent>().Count(e => e.ContestId == contest && !e.Simultaneous && e.SourceSeat == result.SourceSeat) == 1 &&
        ProgramEventHistory<WuLuKangPindianInputRevealedEvent>().Count(e => e.ContestId == contest && e.Seat == result.SourceSeat && e.CardId == result.SourceCardId) == 1 &&
        ProgramEventHistory<PindianResultDeterminedEvent>().Count(e => e.FrameId == contest && e.Result == result) == 1 &&
        (result.SourceCardId == original || ProgramEventHistory<WuLuKangShenduanDiscardPaidEvent>().Any(e => e.ContestId == contest && e.ParticipantSeat == result.SourceSeat) || IsDouyuHistoricalPairAssignment(contest, result, original));
}
