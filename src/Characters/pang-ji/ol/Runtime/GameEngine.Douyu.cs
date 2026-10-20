using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DouyuDrawReason = "skill-program.pang-ji.douyu.draw";
    private bool HasDouyuCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw);
    private static CardConversionSource DouyuSource(ProgramTriggerCandidate c) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pang_ji_ol_Douyu.DouyuSource(c);
    private static CardConversionSource DouyuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pang_ji_ol_Douyu.DouyuSource(f);
    private ResolutionFrame DouyuParent(long id) => _resolutionStack.Single(f => f.Id == id &&
        (f is PindianFrame || f is ProgramSkillFrame { SimultaneousPindian: not null }));

    private IReadOnlyList<DouyuOpportunity> CollectDouyuOpportunities(int source, IReadOnlyList<int> participants)
    {
        var result = new List<DouyuOpportunity>();
        foreach (var seat in participants)
        {
            if (!_players[seat].IsAlive) continue;
            var candidate = GetSkillBindingShard(_players[seat]).ProgramInstances
                .SelectMany(i => i.Program.Triggers.Where(t => t.Id == "pre-reveal" &&
                    t.Window == SkillProgramTriggerWindow.PindianBeforeReveal && t.Effects is [{ Op: SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw }])
                    .Select(t => new ProgramTriggerCandidate(seat, i.SkillId, t.Id, i.SkillInstanceId, i.Program.GameplayHash, t.Priority)))
                .Where(c => HasRuntimeSkillInstance(_players[seat], c.SkillId, c.SkillInstanceId))
                .OrderByDescending(c => c.Priority).ThenBy(c => c.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (candidate is not null) result.Add(new(candidate) { Opponents = seat == source ? participants.Where(s => s != source).ToArray() : [source] });
        }
        return Array.AsReadOnly(result.OrderBy(o => (o.Candidate.OwnerSeat - _turnProgression.OwnerSeat + _players.Count) % _players.Count)
            .ThenByDescending(o => o.Candidate.Priority).ToArray());
    }
    private void CaptureDouyuContest(ResolutionFrame parent, bool simultaneous, int source,
        IReadOnlyList<DouyuOriginalCard> original, IReadOnlyList<DouyuOpportunity> opportunities)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault()?.Id != parent.Id || parent.DouyuContest is not null ||
            original.Count is < 2 or > 4 || original.Select(c => c.Seat).Distinct().Count() != original.Count ||
            original.Select(c => c.CardId).Distinct().Count() != original.Count || original[0].Seat != source ||
            original.Any(c => _cardZones.GetLocation(c.CardId) != c.PaymentFrom))
            throw new InvalidOperationException("Douyu requires the exact complete unrevealed native selection and original payment zones.");
        var receipt = new DouyuContestReceipt(parent.Id, simultaneous, source, _turnNumber, _turnProgression.OwnerSeat, _movementSequence)
        { OriginalCards = original, Assignments = original.Select(c => new DouyuAssignment(c.Seat, c.CardId, c.OriginalRank)).ToArray(), Opportunities = opportunities };
        ReplaceRuntimeFrame(parent.Id, parent with { DouyuContest = receipt });
        AdvanceEventRulesAndQueueFact(new DouyuContestStartedEvent(parent.Id, simultaneous, source, original.Count, _turnNumber, _turnProgression.OwnerSeat, _movementSequence));
        for (var i = 0; i < original.Count; i++) AdvanceEventRulesAndQueueFact(new DouyuOriginalCardCommittedEvent(parent.Id, i, original[i]));
        for (var i = 0; i < opportunities.Count; i++) AdvanceEventRulesAndQueueFact(new DouyuCandidateCommittedEvent(parent.Id, i, DouyuSource(opportunities[i].Candidate), opportunities[i].Candidate.GameplayHash));
        PushRuntimeFrame(new DouyuWindowFrame(++_resolutionSequence, parent.Id, DouyuWindowStage.BeforeReveal, -1,
            opportunities.Select(o => new DouyuWindowEntry(o.Candidate, null)).ToArray()));
        ContinueDouyuWindow(_resolutionStack[^1].Id);
    }
    private bool TryBeginDouyuPairPreReveal(PindianFrame frame, int opponentCardId, bool opponentTop)
    {
        if (frame.DouyuContest is { } existing)
        { if (!existing.Ready || existing.Simultaneous || existing.ParentFrameId != frame.Id || existing.OriginalCards[1].CardId != WuLuKangActualInput(frame, frame.OpponentSeat!.Value, opponentCardId, opponentTop ? CardLocation.DrawPile : CardLocation.Hand(frame.OpponentSeat.Value), 0, opponentTop).CardId || existing.OriginalCards[1].Top != WuLuKangActualInput(frame, frame.OpponentSeat!.Value, opponentCardId, opponentTop ? CardLocation.DrawPile : CardLocation.Hand(frame.OpponentSeat.Value), 0, opponentTop).Top) throw new InvalidOperationException("Douyu cannot replace its original native pair return."); return false; }
        if (!HasDouyuCapability || frame.OpponentSeat is not { } opponent) return false;
        var opportunities = CollectDouyuOpportunities(frame.SourceSeat, [frame.SourceSeat, opponent]);
        if (opportunities.Count == 0) return false;
        AssertOwnedPilePindianSource(frame);
        if (frame.Result is not null || frame.PindianStep != PindianStep.ChooseOpponentCard || frame.SourceCardId is not { } sourceId ||
            !HasCurrentPindianSourceCard(frame)) throw new InvalidOperationException("Douyu requires the native unconsumed pair's two real selections.");
        var from = frame.OwnedPileSource?.Pile.Location ?? (frame.SourceUsesDrawPileTop ? CardLocation.Processing : CardLocation.Hand(frame.SourceSeat));
        var sourceInput = WuLuKangActualInput(frame, frame.SourceSeat, sourceId, from, 0, frame.SourceUsesDrawPileTop);
        from = sourceInput.From; sourceId = sourceInput.CardId;
        var sourceCard = _cardZones.CardsAt(from).Single(c => c.Id == sourceId);
        var opponentFrom = opponentTop ? CardLocation.DrawPile : CardLocation.Hand(opponent);
        var opponentInput = WuLuKangActualInput(frame, opponent, opponentCardId, opponentFrom, 0, opponentTop);
        opponentFrom = opponentInput.From; opponentCardId = opponentInput.CardId;
        var opponentCard = _cardZones.CardsAt(opponentFrom).Single(c => c.Id == opponentCardId);
        if (opponentTop && frame.WuLuKangContest is null && _cardZones.CardsAt(CardLocation.DrawPile).Last().Id != opponentCardId) throw new InvalidOperationException("Douyu cannot choose a different private opponent top.");
        long? reserved = frame.WuLuKangContest is not null ? sourceInput.ReservationSequence : frame.SourceUsesDrawPileTop ? _cardMovements.Last(m => m.CardId == sourceId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == "pindian.reserve-top").Sequence : null;
        CaptureDouyuContest(frame, false, frame.SourceSeat,
            [new(frame.SourceSeat, sourceId, sourceCard.Kind, from, frame.WuLuKangContest is not null ? sourceInput.Rank : CaptureAlcoholPindianRank(frame, _players[frame.SourceSeat], sourceCard, true, frame.SourceUsesDrawPileTop) ?? EffectivePindianRank(_players[frame.SourceSeat], sourceCard), sourceInput.Top, reserved),
             new(opponent, opponentCardId, opponentCard.Kind, opponentFrom, frame.WuLuKangContest is not null ? opponentInput.Rank : CaptureAlcoholPindianRank(frame, _players[opponent], opponentCard, false, opponentTop) ?? EffectivePindianRank(_players[opponent], opponentCard), opponentInput.Top, opponentInput.ReservationSequence)], opportunities);
        return true;
    }
    private bool TryBeginDouyuSimultaneousPreReveal(ProgramSkillFrame frame)
    {
        if (frame.DouyuContest is { } existing)
        { if (!existing.Ready || !existing.Simultaneous || existing.ParentFrameId != frame.Id) throw new InvalidOperationException("Douyu cannot replace its native common-group return."); return false; }
        if (!HasDouyuCapability || frame.SimultaneousPindian is not { Revealed: false } r) return false;
        var participants = new[] { frame.OwnerSeat }.Concat(r.Opponents).ToArray();
        var opportunities = CollectDouyuOpportunities(frame.OwnerSeat, participants); if (opportunities.Count == 0) return false;
        if (r.SelectionIndex != r.Opponents.Count || !r.Cards.Select(c => c.Seat).SequenceEqual(participants))
            throw new InvalidOperationException("Douyu cannot start before every real simultaneous participant has committed.");
        var originals = r.Cards.Select(c => new DouyuOriginalCard(c.Seat, c.CardId, c.CardId <= 0 ? CardKind.Slash : GetAdvancedCard(c.CardId).Kind,
            SimultaneousOriginalLocation(r, c), c.OriginalRank, r.TopReceipts.Any(t => t.CardId == c.CardId),
            r.TopReceipts.SingleOrDefault(t => t.CardId == c.CardId)?.ReservationMovementSequence)).Select(c => WuLuKangDouyuInput(frame, c)).ToArray();
        CaptureDouyuContest(frame, true, frame.OwnerSeat, originals, opportunities); return true;
    }
    private int DouyuAssignedRank(int seat, int cardId)
    {
        var card = GetAdvancedCard(cardId); var actor = _players[seat];
        return card.Kind == CardKind.Alcohol && HasAlcoholKingIdentityPolicy(actor) ? 13 : EffectivePindianRank(actor, card);
    }
    private void FinalizeDouyuAssignments(DouyuWindowFrame window)
    {
        var parent = DouyuParent(window.ParentFrameId); var r = parent.DouyuContest!;
        if (r.Ready || r.Paid || window.Stage != DouyuWindowStage.BeforeReveal || window.CandidateIndex != window.Entries.Count)
            throw new InvalidOperationException("Douyu must finish its unique private opportunity queue before native reveal.");
        r = r with { Ready = true, Assignments = r.Assignments.Select(a => a with { Rank = DouyuAssignedRank(a.Seat, a.CardId) }).ToArray() };
        ReplaceRuntimeFrame(parent.Id, parent with { DouyuContest = r });
        for (var i = 0; i < r.Assignments.Count; i++) { var a = r.Assignments[i]; AdvanceEventRulesAndQueueFact(new DouyuAssignmentFinalizedEvent(parent.Id, i, a.Seat, a.CardId, a.Rank)); }
    }
    private void CommitDouyuNativePayment(ResolutionFrame parent)
    {
        var r = parent.DouyuContest!;
        if (!r.Ready || r.Paid) throw new InvalidOperationException("Douyu native materials can be committed only once after the private queue.");
        foreach (var original in r.OriginalCards)
        {
            if (_cardZones.GetLocation(original.CardId) != CardLocation.Processing) throw new InvalidOperationException("Douyu cannot invent an unpaid native Pindian entity.");
            var reserved = original.PaymentFrom == CardLocation.Processing;
            var movement = reserved ? _cardMovements.Single(m => m.Sequence == original.ReservationSequence && m.CardId == original.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) :
                _cardMovements.Single(m => m.Sequence > r.SequenceBefore && m.CardId == original.CardId && m.CardKind == original.PrintedKind && m.From == original.PaymentFrom && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal);
            AdvanceEventRulesAndQueueFact(new DouyuNativePaymentEvent(parent.Id, original.Seat, original.CardId, original.PrintedKind, original.PaymentFrom, movement.Sequence, reserved));
        }
        ReplaceRuntimeFrame(parent.Id, parent with { DouyuContest = r with { Paid = true, SequenceAfter = _movementSequence } });
    }
    private PindianResult ApplyDouyuPairResult(PindianFrame frame, PindianResult native)
    {
        if (frame.DouyuContest is null) return native;
        var r = DouyuParent(frame.Id).DouyuContest!;
        if (r.Simultaneous || native.SourceSeat != r.SourceSeat || native.SourceCardId != r.OriginalCards[0].CardId ||
            native.OpponentSeat != r.OriginalCards[1].Seat || native.OpponentCardId != r.OriginalCards[1].CardId)
            throw new InvalidOperationException("Douyu must consume the original pair's native payment rather than another contest.");
        CommitDouyuNativePayment(DouyuParent(frame.Id));
        var source = r.Assignments[0]; var opponent = r.Assignments[1];
        return native with { SourceCardId = source.CardId, OpponentCardId = opponent.CardId, SourceRank = source.Rank, OpponentRank = opponent.Rank };
    }
    private IReadOnlyList<PindianRevealedCard> ApplyDouyuSimultaneousCards(ProgramSkillFrame frame, IReadOnlyList<PindianRevealedCard> original)
    {
        if (frame.DouyuContest is null) return original;
        var r = DouyuParent(frame.Id).DouyuContest!;
        if (!r.Simultaneous || !original.Select(c => (c.Seat, c.CardId, c.OriginalRank)).SequenceEqual(r.OriginalCards.Select(c => (c.Seat, c.CardId, c.OriginalRank))))
            throw new InvalidOperationException("Douyu cannot rebind another simultaneous group's material ledger.");
        CommitDouyuNativePayment(DouyuParent(frame.Id));
        return Array.AsReadOnly(r.Assignments.Select(a => new PindianRevealedCard(a.Seat, a.CardId, a.Rank, a.Rank)).ToArray());
    }

    private ProgramSkillWindowContext DouyuContext(DouyuWindowFrame w, ProgramTriggerCandidate c) =>
        new(w.Stage == DouyuWindowStage.BeforeReveal ? SkillProgramTriggerWindow.PindianBeforeReveal : SkillProgramTriggerWindow.PindianResultDetermined,
            w.Id, c.OwnerSeat, SourceSeat: DouyuParent(w.ParentFrameId).DouyuContest!.SourceSeat, TargetSeat: c.OwnerSeat,
            OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]))
        { Douyu = new(w.ParentFrameId, w.Stage, w.ComparisonIndex, w.Entries[w.CandidateIndex].SwapFrameId) };
    private sealed class DouyuTriggerWindowHost(GameEngine engine) : ProgramTriggerWindowHost<DouyuWindowFrame>(engine)
    {
        public override bool IsComplete(DouyuWindowFrame f) => f.CandidateIndex == f.Entries.Count;
        public override ProgramTriggerWindowFlow Close(DouyuWindowFrame f) { Engine.CloseDouyuWindow(f); return ProgramTriggerWindowFlow.Yield; }
        protected override ProgramTriggerCandidate Candidate(DouyuWindowFrame f) => f.Entries[f.CandidateIndex].Candidate;
        public override ProgramTriggerWindowEntry Read(DouyuWindowFrame f) => new(Candidate(f), Engine.DouyuContext(f, Candidate(f)));
        public override void Skip(DouyuWindowFrame f, ProgramTriggerCandidate c) => Engine.SkipDouyuCandidate(f, c);
    }
    private void SkipDouyuCandidate(DouyuWindowFrame window, ProgramTriggerCandidate candidate)
    {
        if (window.CandidateIndex >= window.Entries.Count || window.Entries[window.CandidateIndex].Candidate != candidate)
            throw new InvalidOperationException("Douyu cannot skip a different private or issued result candidate.");
        if (window.Stage == DouyuWindowStage.WinDraw)
        {
            var swap = window.Entries[window.CandidateIndex].SwapFrameId ?? throw new InvalidOperationException("A Douyu win candidate lost its original swap.");
            if (ProgramEventHistory<DouyuRewardClosedEvent>().Any(e => e.ContestId == window.ParentFrameId && e.SwapFrameId == swap))
                throw new InvalidOperationException("Douyu cannot close the same issued win twice.");
            AdvanceEventRulesAndQueueFact(new DouyuRewardClosedEvent(window.ParentFrameId, window.ComparisonIndex, swap, false));
        }
        ReplaceRuntimeTop(window with { CandidateIndex = window.CandidateIndex + 1 });
    }
    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 10, NativeFrameAutomaticPending.RequireNoneBeforeFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(DouyuWindowFrame), 280)]
    private void ContinueDouyuWindow(long id)
    { if (_resolutionStack.LastOrDefault() is not DouyuWindowFrame w || w.Id != id) return; AssertDouyuWindows(); ProgramTriggerWindowDriver.Run(new DouyuTriggerWindowHost(this)); }
    private void CloseDouyuWindow(DouyuWindowFrame w)
    {
        AssertDouyuWindows();
        if (w.Stage == DouyuWindowStage.BeforeReveal) FinalizeDouyuAssignments(w);
        PopResolutionFrame(w.Id, ResolutionFrameKind.DouyuWindow);
        var parent = DouyuParent(w.ParentFrameId);
        if (w.Stage == DouyuWindowStage.BeforeReveal)
        {
            if (parent is PindianFrame pair) { var original = pair.DouyuContest!.OriginalCards[1]; RevealPindian(pair, original.CardId, original.Top); }
            else RevealSimultaneousCommittedGroup((ProgramSkillFrame)parent);
        }
        else if (parent is PindianFrame pair) { if (!BeginPindianClaims(pair)) CompletePindian((PindianFrame)_resolutionStack[^1]); }
        else AdvanceRuntimeProgram(parent.Id);
    }
    [ProgramWindowReturn(SkillProgramTriggerWindow.PindianBeforeReveal, SkillProgramTriggerWindow.PindianResultDetermined)]
    private void ReturnDouyuBinding(ProgramSkillFrame frame, bool completed)
    {
        if (_resolutionStack.LastOrDefault() is not DouyuWindowFrame w || frame.WindowContext is not { Douyu: { } c } context ||
            w.Id != context.ParentFrameId || w.ParentFrameId != c.ContestId || w.CandidateIndex >= w.Entries.Count ||
            !MountObserverCandidateMatches(frame, w.Entries[w.CandidateIndex].Candidate))
            throw new InvalidOperationException("Douyu lost its exact native private/result window return.");
        ReplaceRuntimeTop(w with { CandidateIndex = w.CandidateIndex + 1 }); ContinueDouyuWindow(w.Id);
    }
    [ProgramCapability] private sealed class DouyuCapability(GameEngine engine) : IDouyuHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.ResolveDouyu(frame); }
    private SkillProgramStepOutcome ResolveDouyu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Douyu is not null || f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { Douyu: { } context } c ||
            !IsIssuedDouyuCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c))
            throw new InvalidOperationException("Douyu requires its exact native committed-selection or issued swapped-win binding.");
        var r = DouyuParent(context.ContestId).DouyuContest!;
        var issued = context.SwapFrameId is { } swapId ? r.Swaps.Single(s => s.FrameId == swapId).Source : DouyuSource(f);
        var receipt = new DouyuReceipt(DouyuSource(f), issued, f.GameplayHash, context.ContestId, c.ParentFrameId,
            context.ComparisonIndex, context.SwapFrameId, context.Stage == DouyuWindowStage.BeforeReveal ? DouyuStage.OpponentChoice : DouyuStage.DrawWaiting);
        ReplaceRuntimeTop(f = f with { Douyu = receipt });
        if (context.Stage == DouyuWindowStage.BeforeReveal) { PublishDouyu(f); return SkillProgramStepOutcome.AwaitChoice; }
        return BeginDouyuWinDraw(f);
    }
    private IReadOnlyList<PromptChoice> DouyuChoices(ProgramSkillFrame f)
    {
        if (f.Douyu is not { Stage: DouyuStage.OpponentChoice } r) return [];
        var contest = DouyuParent(r.ContestId).DouyuContest!;
        var opportunity = contest.Opportunities.Single(o => o.Candidate.OwnerSeat == f.OwnerSeat && o.Candidate.SkillInstanceId == f.SkillInstanceId);
        return Array.AsReadOnly(opportunity.Opponents.Where(s => _players[s].IsAlive).Select(seat => PromptChoiceProjection.Freeze(new PromptChoice(
            new($"douyu.{f.Id}.swap.{seat}"), "与" + _players[seat].Name + "交换尚未亮出的拼点牌", [], [seat],
            new Dictionary<string, string> { ["program-action"] = "douyu", ["branch"] = "swap", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) }))).ToArray());
    }
    private void PublishDouyu(ProgramSkillFrame f)
    {
        var choices = DouyuChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || choices.Count == 0)
        { ReplaceRuntimeTop(f with { Douyu = null }); FinishProgramSkill(f, false); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "斗誉：选择真实拼点对手；双方拼点牌尚未公开。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { IsPrivate = true, PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("douyu")]
    private void AnswerDouyu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Douyu: { Stage: DouyuStage.OpponentChoice } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !DouyuChoices(f).Any(c => AssistedChoicesEqual([c], [choice])) ||
            !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Douyu requires its actual owner's private published opponent choice.");
        var parent = DouyuParent(r.ContestId); var contest = parent.DouyuContest!; var opponent = choice.Targets.Single();
        var ownerCard = contest.Assignments.Single(a => a.Seat == f.OwnerSeat); var otherCard = contest.Assignments.Single(a => a.Seat == opponent);
        var swap = new DouyuSwap(f.Id, r.WindowId, contest.Swaps.Count, r.Source, r.GameplayHash, opponent,
            ownerCard.CardId, otherCard.CardId, otherCard.CardId, ownerCard.CardId);
        ClearPendingDecision();
        ReplaceRuntimeFrame(parent.Id, parent with { DouyuContest = contest with { Swaps = contest.Swaps.Append(swap).ToArray(),
            Assignments = contest.Assignments.Select(a => a.Seat == f.OwnerSeat ? a with { CardId = otherCard.CardId } : a.Seat == opponent ? a with { CardId = ownerCard.CardId } : a).ToArray() } });
        AdvanceEventRulesAndQueueFact(new DouyuSwapCommittedEvent(parent.Id, r.WindowId, f.Id, swap.Ordinal, r.Source, r.GameplayHash,
            opponent, swap.OwnerCardBefore, swap.OpponentCardBefore, swap.OwnerCardAfter, swap.OpponentCardAfter));
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, Douyu = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveDouyuPreRevealSwapAndWinDraw)]
    private PromptChoice ChooseDouyuAi(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
}
