using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuLuKangDiscardReason = "skill-program.wu-lu-kang.shenduan.discard", WuLuKangReserveReason = "skill-program.wu-lu-kang.shenduan.reserve",
        WuLuKangBottomReason = "skill-program.wu-lu-kang.shenduan.bottom", WuLuKangDrawReason = "skill-program.wu-lu-kang.shenduan.draw";
    private bool HasWuLuKangCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuKangShenduan) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuKangKegou) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWuLuKangDixian);
    private static CardConversionSource WuLuKangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardConversionSource WuLuKangSource(ProgramTriggerCandidate c) => new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId);
    private bool CanWuLuKangDiscardForPindian(CharacterState owner) => owner.IsAlive && CardPolicies(owner, SkillProgramCardPolicyKind.PindianDiscardForMaximumDeck).Any(binding =>
        GetHand(owner).Count > 0 || GetEquipment(owner).Any(card => !IsForeignEquipmentDiscardPrevented(owner.Seat, card, CardLocation.Equipment(owner.Seat), OwnedCardMoveIntent.Discard) &&
            !IsActiveProgramSourceEquipmentCard(owner.Seat, binding.Source.SkillId, binding.Source.SkillInstanceId, card)));
    private bool HasWuLuKangPindianInput(CharacterState owner) => GetHand(owner).Count > 0 || CanWuLuKangDiscardForPindian(owner);
    private ResolutionFrame WuLuKangParent(long id) => _resolutionStack.Single(f => f.Id == id && (f is PindianFrame || f is ProgramSkillFrame { SimultaneousPindian: not null }));
    private IReadOnlyList<ProgramTriggerCandidate> WuLuKangSources(int? onlySeat = null) => Array.AsReadOnly(_players.Where(p => p.IsAlive && (onlySeat is null || p.Seat == onlySeat))
        .SelectMany(p => GetSkillBindingShard(p).ProgramInstances.SelectMany(i => i.Program.Triggers.Where(t => t.Window == SkillProgramTriggerWindow.WuLuKangPindian &&
            t.Effects is [{ Op: SkillProgramEffectOp.ResolveWuLuKangShenduan }]).Select(t => new ProgramTriggerCandidate(p.Seat, i.SkillId, t.Id, i.SkillInstanceId, i.Program.GameplayHash, t.Priority))))
        .OrderBy(c => (c.OwnerSeat - _turnProgression.OwnerSeat + _players.Count) % _players.Count).ThenByDescending(c => c.Priority).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).ToArray());
    private int WuLuKangTurnOrdinal => CompleteProgramEventHistory().OfType<TurnStartedEvent>().Count();
    private void CaptureWuLuKangContest(ResolutionFrame parent, bool group, int source, IReadOnlyList<WuLuKangPindianInput> inputs)
    {
        if (_resolutionStack.LastOrDefault()?.Id != parent.Id || parent.WuLuKangContest is not null || inputs.Count is < 2 or > 4 || inputs[0].Seat != source ||
            inputs.Select(c => c.Seat).Distinct().Count() != inputs.Count) throw new InvalidOperationException("Shenduan requires the complete native private commitments.");
        var committed = inputs.ToArray();
        for (var i = 0; i < committed.Length; i++)
        {
            var input = committed[i];
            if (input.Top && input.From == CardLocation.DrawPile)
            {
                var card = GetAdvancedCard(input.CardId); var sequence = _movementSequence + 1;
                committed[i] = input with { From = CardLocation.Processing, ReservationSequence = sequence };
                MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, new(WuLuKangReserveReason), emitCardMovedEvents: false, disclosure: CardMovementDisclosure.FaceDown);
            }
        }
        inputs = Array.AsReadOnly(committed);
        var r = new WuLuKangContestReceipt(parent.Id, group, source, _turnNumber, _turnProgression.OwnerSeat, WuLuKangTurnOrdinal)
        { Original = inputs, Assignments = inputs.Select(c => new WuLuKangPindianAssignment(c.Seat, c.CardId, c.Rank)).ToArray() };
        ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangContest = r });
        AdvanceEventRulesAndQueueFact(new WuLuKangContestStartedEvent(parent.Id, group, source, inputs.Count, r.ActualTurn, r.TurnOwner, r.TurnOrdinal));
        var entries = inputs.SelectMany(c => WuLuKangSources(c.Seat).Select(s => new WuLuKangPindianWindowEntry(s, c.Seat))).ToArray();
        BeginWuLuKangWindow(parent.Id, WuLuKangPindianStage.BeforeSourceReplacement, -1, entries, null);
    }
    private bool TryBeginWuLuKangPairBeforeReveal(PindianFrame f, int opponentCardId, bool top)
    {
        if (f.WuLuKangContest is { } existing) { if (!existing.SourcesReady || existing.Simultaneous || existing.Original[1].CardId != opponentCardId || existing.Original[1].Top != top) throw new InvalidOperationException("Shenduan cannot replace its original pair continuation."); return false; }
        if (!HasWuLuKangCapability || f.OpponentSeat is not { } other) return false;
        var sourceFrom = f.OwnedPileSource?.Pile.Location ?? (f.SourceUsesDrawPileTop ? CardLocation.Processing : CardLocation.Hand(f.SourceSeat));
        WuLuKangPindianInput Input(int seat, int id, CardLocation from, bool isTop)
        {
            if (id <= 0) return new(seat, id, CardKind.Slash, from, 0, false, null);
            var card = GetAdvancedCard(id); var rank = CaptureAlcoholPindianRank(f, _players[seat], card, seat == f.SourceSeat, isTop) ?? EffectivePindianRank(_players[seat], card);
            return new(seat, id, card.Kind, from, rank, isTop, from == CardLocation.Processing ? _cardMovements.Last(m => m.CardId == id && m.To == CardLocation.Processing).Sequence : null);
        }
        CaptureWuLuKangContest(f, false, f.SourceSeat, [Input(f.SourceSeat, f.SourceCardId ?? 0, sourceFrom, f.SourceUsesDrawPileTop), Input(other, opponentCardId, top ? CardLocation.DrawPile : CardLocation.Hand(other), top)]);
        return true;
    }
    private bool TryBeginWuLuKangGroupBeforeReveal(ProgramSkillFrame f)
    {
        if (f.WuLuKangContest is { } existing) { if (!existing.SourcesReady || !existing.Simultaneous) throw new InvalidOperationException("Shenduan cannot reenter an unfinished group selection."); return false; }
        if (!HasWuLuKangCapability || f.SimultaneousPindian is not { Revealed: false } r) return false;
        CaptureWuLuKangContest(f, true, f.OwnerSeat, r.Cards.Select(c => new WuLuKangPindianInput(c.Seat, c.CardId, c.CardId <= 0 ? CardKind.Slash : GetAdvancedCard(c.CardId).Kind,
            SimultaneousOriginalLocation(r, c), c.OriginalRank, r.TopReceipts.Any(t => t.CardId == c.CardId), r.TopReceipts.SingleOrDefault(t => t.CardId == c.CardId)?.ReservationMovementSequence)).ToArray()); return true;
    }
    private void BeginWuLuKangWindow(long parent, WuLuKangPindianStage stage, int comparison, IReadOnlyList<WuLuKangPindianWindowEntry> entries, PindianResult? result)
    {
        var w = new WuLuKangPindianFrame(++_resolutionSequence, parent, stage, comparison, entries, result);
        PushRuntimeFrame(w); AdvanceEventRulesAndQueueFact(new WuLuKangWindowOpenedEvent(w.Id, parent, stage, comparison, entries.Count));
        for (var i = 0; i < entries.Count; i++) AdvanceEventRulesAndQueueFact(new WuLuKangWindowCandidateEvent(w.Id, i, WuLuKangSource(entries[i].Candidate), entries[i].Candidate.GameplayHash, entries[i].ParticipantSeat));
        ContinueWuLuKangWindow(w.Id);
    }
    private bool TryBeginWuLuKangPostComparison(ResolutionFrame parent, PindianResult result, int comparison)
    {
        if (!HasWuLuKangCapability) return false;
        if (ProgramEventHistory<WuLuKangComparisonSettledEvent>().Any(e => e.ContestId == parent.Id && e.ComparisonIndex == comparison)) return false;
        if (parent.WuLuKangContest is null) throw new InvalidOperationException("Shenduan result requires its original native selection receipt.");
        var r = parent.WuLuKangContest with { Revealed = true, Comparisons = parent.WuLuKangContest.Comparisons.Append(new WuLuKangSettledComparison(comparison, result)).ToArray() };
        ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangContest = r }); AdvanceEventRulesAndQueueFact(new WuLuKangComparisonSettledEvent(parent.Id, comparison, result));
        BeginWuLuKangWindow(parent.Id, WuLuKangPindianStage.PostComparison, comparison, WuLuKangSources().Select(c => new WuLuKangPindianWindowEntry(c, c.OwnerSeat)).ToArray(), result); return true;
    }
    private ProgramSkillWindowContext WuLuKangContext(WuLuKangPindianFrame w, ProgramTriggerCandidate c) => new(SkillProgramTriggerWindow.WuLuKangPindian,
        w.Id, c.OwnerSeat, SourceSeat: WuLuKangParent(w.ParentFrameId).WuLuKangContest!.SourceSeat, TargetSeat: c.OwnerSeat,
        OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]))
        { WuLuKangPindian = new(w.ParentFrameId, w.Stage, w.ComparisonIndex, w.Entries[w.CandidateIndex].ParticipantSeat) };
    private sealed class WuLuKangWindowHost(GameEngine engine) : ProgramTriggerWindowHost<WuLuKangPindianFrame>(engine)
    {
        protected override ProgramTriggerCandidate Candidate(WuLuKangPindianFrame w) => w.Entries[w.CandidateIndex].Candidate;
        public override bool IsComplete(WuLuKangPindianFrame w) => w.CandidateIndex == w.Entries.Count;
        public override ProgramTriggerWindowEntry Read(WuLuKangPindianFrame w) => new(Candidate(w), Engine.WuLuKangContext(w, Candidate(w)));
        public override void Skip(WuLuKangPindianFrame w, ProgramTriggerCandidate c) => Engine.ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 });
        public override ProgramTriggerWindowFlow Close(WuLuKangPindianFrame w) { Engine.CloseWuLuKangWindow(w); return ProgramTriggerWindowFlow.Yield; }
    }
    private void ContinueWuLuKangWindow(long id)
    { if (_resolutionStack.LastOrDefault() is WuLuKangPindianFrame w && w.Id == id) { AssertWuLuKangWindows(); ProgramTriggerWindowDriver.Run(new WuLuKangWindowHost(this)); } }
    private void CloseWuLuKangWindow(WuLuKangPindianFrame w)
    {
        var parent = WuLuKangParent(w.ParentFrameId); var r = parent.WuLuKangContest!;
        if (w.Stage == WuLuKangPindianStage.BeforeSourceReplacement)
        {
            foreach (var input in r.Original.Where(c => c.From == CardLocation.Processing && r.Assignments.All(a => a.CardId != c.CardId)))
                if (_cardZones.GetLocation(input.CardId) == CardLocation.Processing) MoveCard(GetAdvancedCard(input.CardId), CardLocation.Processing, CardLocation.DrawPile, new(WuLuKangReserveReason), emitCardMovedEvents: false, disclosure: CardMovementDisclosure.FaceDown);
            r = r with { SourcesReady = true, Assignments = r.Assignments.Select(a => a.CardId <= 0 ? a : a with { Rank = WuLuKangFinalInputRank(r, a) }).ToArray() }; ReplaceRuntimeFrame(parent.Id, parent = parent with { WuLuKangContest = r }); AdvanceEventRulesAndQueueFact(new WuLuKangSourcesClosedEvent(parent.Id));
        }
        AdvanceEventRulesAndQueueFact(new WuLuKangWindowClosedEvent(w.Id, parent.Id, w.Stage, w.ComparisonIndex)); PopResolutionFrame(w.Id, ResolutionFrameKind.WuLuKangPindian);
        if (w.Stage == WuLuKangPindianStage.BeforeSourceReplacement && !r.Cancelled && (!WuLuKangInputsAvailable(parent) || r.Assignments.Any(a => a.CardId <= 0)))
        {
            MarkWuLuKangCancelled(parent.Id, "unpaid-source-window-exhausted"); parent = WuLuKangParent(parent.Id); r = parent.WuLuKangContest!;
        }
        if (r.Cancelled)
        {
            if (WuLuKangHeldProcessing(parent).Count > 0)
            {
                var cleanup = w.Entries.FirstOrDefault() ?? throw new InvalidOperationException("A paid cancelled contest lost its original source candidate.");
                BeginWuLuKangWindow(parent.Id, WuLuKangPindianStage.GroupClose, -1, [cleanup], null); return;
            }
            CancelWuLuKangContest(parent); return;
        }
        if (w.Stage == WuLuKangPindianStage.BeforeSourceReplacement)
        {
            if (parent is PindianFrame pair) { var input = r.Original[1]; RevealPindian(pair, input.CardId, input.Top); }
            else RevealSimultaneousCommittedGroup((ProgramSkillFrame)parent);
        }
        else if (parent is PindianFrame pair) { if (!BeginPindianClaims(pair)) CompletePindian((PindianFrame)_resolutionStack[^1]); }
        else AdvanceRuntimeProgram(parent.Id);
    }
    [ProgramWindowReturn(SkillProgramTriggerWindow.WuLuKangPindian)]
    private void ReturnWuLuKangBinding(ProgramSkillFrame f, bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not WuLuKangPindianFrame w || f.WindowContext?.WuLuKangPindian is not { } c ||
            f.WindowContext.ParentFrameId != w.Id || c.ContestId != w.ParentFrameId || c.Stage != w.Stage || c.ComparisonIndex != w.ComparisonIndex ||
            w.CandidateIndex >= w.Entries.Count || !MountObserverCandidateMatches(f, w.Entries[w.CandidateIndex].Candidate)) throw new InvalidOperationException("Shenduan returned to a different comparison or native participant.");
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); ContinueWuLuKangWindow(w.Id);
    }
    private bool IsIssuedWuLuKangCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.WuLuKangPindian is not { } c || context.Window != SkillProgramTriggerWindow.WuLuKangPindian || context.OwnerSeat != candidate.OwnerSeat ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not WuLuKangPindianFrame w || w.ParentFrameId != c.ContestId || w.Stage != c.Stage ||
            w.ComparisonIndex != c.ComparisonIndex || w.CandidateIndex < 0 || w.CandidateIndex >= w.Entries.Count || w.Entries[w.CandidateIndex] != new WuLuKangPindianWindowEntry(candidate, c.ParticipantSeat) ||
            _resolutionStack.SingleOrDefault(f => f.Id == c.ContestId) is not { WuLuKangContest: { } r } parent || !ValidWuLuKangContest(parent) ||
            context.SourceSeat != r.SourceSeat || context.TargetSeat != candidate.OwnerSeat || context.OccurrenceIndex != candidate.OccurrenceIndex ||
            _contentRegistry.GetSkill(candidate.SkillId).Program is not { } p || p.GameplayHash != candidate.GameplayHash || !WuLuKangComposition.IsShenduan(p) ||
            candidate.OccurrenceIndex != 0 || candidate.Priority != p.Triggers[0].Priority ||
            ProgramEventHistory<WuLuKangWindowCandidateEvent>().Count(e => e == new WuLuKangWindowCandidateEvent(w.Id, w.CandidateIndex, WuLuKangSource(candidate), candidate.GameplayHash, c.ParticipantSeat)) != 1) return false;
        if (r.Cancelled && w.Stage != WuLuKangPindianStage.GroupClose) return false;
        if (w.Stage == WuLuKangPindianStage.GroupClose) return r.Cancelled && ProgramEventHistory<WuLuKangWindowClosedEvent>().Any(e => e.ContestId == parent.Id && e.Stage == WuLuKangPindianStage.BeforeSourceReplacement);
        return _players[candidate.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId) && (w.Stage == WuLuKangPindianStage.PostComparison || c.ParticipantSeat == candidate.OwnerSeat && r.Original.Any(i => i.Seat == c.ParticipantSeat) &&
            (GetHand(_players[candidate.OwnerSeat]).Count > 0 || GetEquipment(_players[candidate.OwnerSeat]).Any(card => !IsForeignEquipmentDiscardPrevented(candidate.OwnerSeat, card, CardLocation.Equipment(candidate.OwnerSeat), OwnedCardMoveIntent.Discard) &&
                !IsActiveProgramSourceEquipmentCard(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId, card))) &&
            !ProgramEventHistory<WuLuKangShenduanStartedEvent>().Any(e => e.ContestId == parent.Id && e.ParticipantSeat == c.ParticipantSeat && e.Stage == WuLuKangPindianStage.BeforeSourceReplacement));
    }
    private WuLuKangPindianInput WuLuKangActualInput(ResolutionFrame parent, int seat, int id, CardLocation from, int rank, bool top)
    {
        if (parent.WuLuKangContest is not { SourcesReady: true } r) return new(seat, id, GetAdvancedCard(id).Kind, from, rank, top, null);
        var a = r.Assignments.Single(i => i.Seat == seat); var replacement = r.Replacements.LastOrDefault(p => p.ParticipantSeat == seat && p.ReservedCardId == a.CardId);
        return replacement is null ? r.Original.Single(i => i.Seat == seat) with { Rank = a.Rank } : new(seat, a.CardId, GetAdvancedCard(a.CardId).Kind, CardLocation.Processing, a.Rank, false, replacement.ReservationSequence);
    }
    private int WuLuKangFinalInputRank(WuLuKangContestReceipt contest, WuLuKangPindianAssignment assignment)
    {
        var owner = _players[assignment.Seat]; var card = GetAdvancedCard(assignment.CardId);
        var stockSource = contest.Original.Single(c => c.Seat == assignment.Seat).Top ||
            contest.Replacements.Any(p => p.ParticipantSeat == assignment.Seat && p.ReservedCardId == assignment.CardId);
        return stockSource && card.Kind == CardKind.Alcohol && HasAlcoholKingIdentityPolicy(owner) ? 13 : EffectivePindianRank(owner, card);
    }
    private DouyuOriginalCard WuLuKangDouyuInput(ResolutionFrame parent, DouyuOriginalCard original)
    {
        if (parent.WuLuKangContest is not { SourcesReady: true }) return original;
        var input = WuLuKangActualInput(parent, original.Seat, original.CardId, original.PaymentFrom, original.OriginalRank, original.Top);
        return new(input.Seat, input.CardId, input.PrintedKind, input.From, input.Rank, input.Top, input.ReservationSequence);
    }
    private bool IsWuLuKangOriginalInput(ResolutionFrame parent, int seat, int card, CardLocation from, bool top, long? sequence)
    {
        if (parent.WuLuKangContest is not { SourcesReady: true } r || !ValidWuLuKangContest(parent)) return false;
        var a = r.Assignments.SingleOrDefault(i => i.Seat == seat); if (a is null || a.CardId != card) return false;
        var input = WuLuKangActualInput(parent, seat, card, from, a.Rank, top);
        return input.CardId == card && input.From == from && input.Top == top && input.ReservationSequence == sequence;
    }
    [ProgramViewContributor(ProgramViewContribution.ConcealedCardExclusion, 20)]
    private bool IsWuLuKangPrivatePindianCard(int id) => _resolutionStack.Any(f => f.WuLuKangContest is { Revealed: false, Cancelled: false } r &&
        (r.Replacements.Any(p => p.ReservedCardId == id) || r.Original.Any(p => p.CardId == id && p.From == CardLocation.Processing)));
    private IReadOnlyList<int> WuLuKangHeldProcessing(ResolutionFrame parent) => parent.WuLuKangContest is not { } r ? [] :
        r.Original.Select(c => c.CardId).Concat(r.Replacements.Where(p => p.ReservedCardId is not null).Select(p => p.ReservedCardId!.Value)).Where(id => id > 0 && _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().ToArray();
    private void RevealWuLuKangInputs(ResolutionFrame parent)
    {
        if (parent.WuLuKangContest is not { Revealed: false } r) return;
        ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangContest = r with { Revealed = true } });
        foreach (var a in r.Assignments) AdvanceEventRulesAndQueueFact(new WuLuKangPindianInputRevealedEvent(parent.Id, a.Seat, a.CardId, a.Rank, r.Replacements.Any(p => p.ReservedCardId == a.CardId)));
    }
    private void MarkWuLuKangCancelled(long contest, string reason)
    {
        var parent = WuLuKangParent(contest); var r = parent.WuLuKangContest!;
        ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangContest = r with { Cancelled = true, CancellationReason = reason } });
    }
    private void CancelWuLuKangContest(ResolutionFrame parent)
    {
        var r = parent.WuLuKangContest!;
        if (!r.Cancelled || WuLuKangHeldProcessing(parent).Count != 0) throw new InvalidOperationException("Cancelled Pindian must finish its exact native material cleanup first.");
        var parentId = parent is PindianFrame pair ? pair.ParentFrameId : parent.Id;
        AdvanceEventRulesAndQueueFact(new WuLuKangPindianCancelledEvent(parent.Id, parentId, r.Simultaneous, r.CancellationReason ?? "participant-unavailable"));
        if (parent is PindianFrame p)
        {
            PopResolutionFrame(p.Id, ResolutionFrameKind.Pindian);
            var owner = GetActiveProgramFrame(p.ParentFrameId);
            if (owner.WuLuKangKegou is { } kegou) { ReplaceRuntimeTop(owner with { WuLuKangKegou = kegou with { PindianCancelled = true } }); ContinueWuLuKangKegou(owner.Id); }
            else { CancelProgramBindingAndCleanup(owner, "拼点参与者或尚未支付的拼点牌已不可用。"); }
        }
        else
        {
            var group = (ProgramSkillFrame)parent; ReplaceRuntimeTop(group = group with { SimultaneousPindian = null, PendingMovementContinuation = null });
            CancelProgramBindingAndCleanup(group, "同时拼点的参与者或尚未支付的拼点牌已不可用。");
        }
    }
}
