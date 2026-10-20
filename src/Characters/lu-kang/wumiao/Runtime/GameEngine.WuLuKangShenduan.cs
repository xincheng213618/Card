using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability] private sealed class WuLuKangCapability(GameEngine engine) : IWuLuKangHost
    {
        public SkillProgramStepOutcome Shenduan(ProgramSkillFrame f) => engine.BeginWuLuKangShenduan(f);
        public SkillProgramStepOutcome Kegou(ProgramSkillFrame f) => engine.BeginWuLuKangKegou(f);
    }
    private SkillProgramStepOutcome BeginWuLuKangShenduan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.WuLuKangShenduan is not null || f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { WuLuKangPindian: { } c } context ||
            !IsIssuedWuLuKangCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context))
            throw new InvalidOperationException("Shenduan can start only in its exact private-source or settled-comparison window.");
        var parent = WuLuKangParent(c.ContestId); var w = (WuLuKangPindianFrame)_resolutionStack[^2];
        var recipients = w.Result is { } p ? new[] { (p.SourceSeat, p.SourceRank), (p.OpponentSeat, p.OpponentRank) }.Where(i => i.Item2 == 13).Select(i => i.Item1).Distinct().ToArray() : [];
        var r = new WuLuKangShenduanReceipt(WuLuKangSource(f), f.GameplayHash, context.ParentFrameId, c.ContestId, c.Stage, c.ComparisonIndex, c.ParticipantSeat)
        { Stage = c.Stage == WuLuKangPindianStage.BeforeSourceReplacement ? WuLuKangShenduanStage.DiscardChoice : WuLuKangShenduanStage.BottomChildren, Recipients = recipients, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r });
        AdvanceEventRulesAndQueueFact(new WuLuKangShenduanStartedEvent(f.Id, r.WindowId, r.ContestId, r.Source, r.GameplayHash, r.WindowStage, r.ComparisonIndex, r.ParticipantSeat));
        if (c.Stage == WuLuKangPindianStage.GroupClose)
        { CancelWuLuKangFromShenduan(f, parent.WuLuKangContest!.CancellationReason ?? "participant-unavailable"); return SkillProgramStepOutcome.AwaitChild; }
        if (r.Stage == WuLuKangShenduanStage.DiscardChoice)
        {
            if (parent.WuLuKangContest!.Cancelled) { FinishWuLuKangShenduan(f); return SkillProgramStepOutcome.AwaitChild; }
            var choices = WuLuKangShenduanChoices(f);
            if (choices.Count == 0) { CancelWuLuKangFromShenduan(f, "unpaid-source-unavailable"); return SkillProgramStepOutcome.AwaitChild; }
            PublishWuLuKang(f, f.OwnerSeat, choices, "审断：弃置一张手牌或装备牌，以牌堆中点数最大的牌替代本次拼点牌。"); return SkillProgramStepOutcome.AwaitChoice;
        }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (w.Result is { } result && result.SourceRank != result.OpponentRank)
        {
            var winner = result.SourceWon ? result.SourceSeat : result.OpponentSeat; var id = result.CardOf(winner);
            if (_cardZones.GetLocation(id) == CardLocation.Processing)
            {
                var before = _movementSequence;
                MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DrawPile, new(WuLuKangBottomReason), m =>
                {
                    var current = GetActiveProgramFrame(f.Id); var contest = WuLuKangParent(r.ContestId); var receipt = contest.WuLuKangContest!;
                    ReplaceRuntimeFrame(contest.Id, contest with { WuLuKangContest = receipt with { BottomedCardIds = receipt.BottomedCardIds.Append(id).ToArray() } });
                    AdvanceEventRulesAndQueueFact(new WuLuKangWinnerBottomedEvent(f.Id, r.ContestId, r.ComparisonIndex, winner, id, m.Sequence));
                });
                _cardZones.PlaceDrawPileCardsAtBottom([id]); SealWuLuKangMovements(f.Id, before);
            }
        }
        ContinueWuLuKangShenduan(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void PublishWuLuKang(ProgramSkillFrame f, int chooser, IReadOnlyList<PromptChoice> choices, string text)
    {
        if (choices.Count == 0) throw new InvalidOperationException("Wu Lu Kang lost its exact legal published choices.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    private IReadOnlyList<PromptChoice> WuLuKangShenduanChoices(ProgramSkillFrame f)
    {
        var r = f.WuLuKangShenduan!; var parent = WuLuKangParent(r.ContestId); var assigned = parent.WuLuKangContest!.Assignments.Single(a => a.Seat == r.ParticipantSeat);
        Dictionary<string, string> P(string branch) => new() { ["program-action"] = "wu-lu-kang-shenduan", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        var choices = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard)
            .Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "wu-lu-kang-shenduan", ["branch"] = "discard", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["zone"] = c.Parameters["source-zone"].ToLowerInvariant() } }).ToList();
        choices.Add(new(new($"wu-lu-kang.{f.Id}.decline"), assigned.CardId > 0 ? "不替换拼点牌" : "不弃牌并取消本次拼点", [], [], P("decline")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private CardLocation WuLuKangAssignedLocation(ResolutionFrame parent, int seat)
    {
        var r = parent.WuLuKangContest!; var a = r.Assignments.Single(i => i.Seat == seat);
        return r.Replacements.Any(p => p.ParticipantSeat == seat && p.ReservedCardId == a.CardId) ? CardLocation.Processing : r.Original.Single(i => i.Seat == seat).From;
    }
    [ProgramAnswer("wu-lu-kang-shenduan")]
    private void AnswerWuLuKangShenduan(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.WuLuKangShenduan!;
        if (!ValidWuLuKangShenduan(f) || r.Stage != WuLuKangShenduanStage.DiscardChoice || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], WuLuKangShenduanChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Shenduan requires its original participant's actual HE discard slot.");
        ClearPendingDecision();
        if (selected.Parameters["branch"] == "decline")
        {
            var contest = WuLuKangParent(r.ContestId);
            if (contest.WuLuKangContest!.Assignments.Single(a => a.Seat == r.ParticipantSeat).CardId <= 0) CancelWuLuKangFromShenduan(f, "discard-backed-source-declined");
            else FinishWuLuKangShenduan(f); return;
        }
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), f.OwnerSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)]; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.DiscardChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, f.OwnerSeat, card, from, new(WuLuKangDiscardReason));
        f = GetActiveProgramFrame(f.Id); var movement = _cardMovements.Single(m => m.Sequence > before && m.CardId == card.Id && m.From == from && m.Reason.Value == WuLuKangDiscardReason);
        var payment = new WuLuKangShenduanPayment(f.Id, r.Source, r.GameplayHash, r.ParticipantSeat, card.Id, card.Kind, from, movement.To, movement.Sequence, before, _movementSequence);
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = f.WuLuKangShenduan! with { Payment = payment } });
        var parent = WuLuKangParent(r.ContestId); ReplaceRuntimeFrame(parent.Id, parent with { WuLuKangContest = parent.WuLuKangContest! with { Replacements = parent.WuLuKangContest.Replacements.Append(payment).ToArray() } });
        AdvanceEventRulesAndQueueFact(new WuLuKangShenduanDiscardPaidEvent(f.Id, r.ContestId, r.ParticipantSeat, card.Id, card.Kind, from, movement.To, movement.Sequence));
        SealWuLuKangMovements(f.Id, before); ContinueWuLuKangShenduan(f.Id);
    }
    private bool WuLuKangInputsAvailable(ResolutionFrame parent, int? replacing = null) => parent.WuLuKangContest!.Assignments.All(a =>
        _players[a.Seat].IsAlive && (a.Seat == replacing || a.CardId > 0 && _cardZones.GetLocation(a.CardId) == WuLuKangAssignedLocation(parent, a.Seat) ||
            a.CardId <= 0 && !parent.WuLuKangContest.SourcesReady && !parent.WuLuKangContest.Replacements.Any(p => p.ParticipantSeat == a.Seat) && CanWuLuKangDiscardForPindian(_players[a.Seat])));
    private void ContinueWuLuKangShenduan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangShenduan: { } r } f || f.Id != id) return;
        AssertWuLuKang(f); if (r.Stage is WuLuKangShenduanStage.DiscardChoice or WuLuKangShenduanStage.Drawing) return;
        if (DrainWuLuKangChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.WuLuKangShenduan!; var parent = WuLuKangParent(r.ContestId);
        if (r.Stage == WuLuKangShenduanStage.CancelChildren) { FinishWuLuKangShenduan(f); return; }
        if (r.Stage == WuLuKangShenduanStage.DiscardChildren)
        {
            if (_winner != Winner.None || !WuLuKangInputsAvailable(parent, r.ParticipantSeat)) { CancelWuLuKangFromShenduan(f, "participant-or-unpaid-input-unavailable"); return; }
            ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.RefillChildren, Before = _movementSequence, After = _movementSequence } });
            var before = _movementSequence; EnsureDrawPile(); SealWuLuKangMovements(id, before); ContinueWuLuKangShenduan(id); return;
        }
        if (r.Stage == WuLuKangShenduanStage.RefillChildren)
        {
            if (_winner != Winner.None || !WuLuKangInputsAvailable(parent, r.ParticipantSeat) || _cardZones.Count(CardLocation.DrawPile) == 0) { CancelWuLuKangFromShenduan(f, "maximum-stock-unavailable"); return; }
            var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().OrderByDescending(c => c.Rank).First(); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.ReserveChildren, Before = before, After = before } });
            MoveCard(card, CardLocation.DrawPile, CardLocation.Processing, new(WuLuKangReserveReason), m =>
            {
                var current = GetActiveProgramFrame(id); var paid = current.WuLuKangShenduan!.Payment! with { ReservedCardId = m.CardId, ReservedPrintedRank = card.Rank, ReservationSequence = m.Sequence, After = m.Sequence };
                ReplaceRuntimeTop(current with { WuLuKangShenduan = current.WuLuKangShenduan with { Payment = paid, After = m.Sequence } });
                var contest = WuLuKangParent(r.ContestId); var receipt = contest.WuLuKangContest!;
                var effective = card.Kind == CardKind.Alcohol && HasAlcoholKingIdentityPolicy(_players[r.ParticipantSeat]) ? 13 : EffectivePindianRank(_players[r.ParticipantSeat], card);
                ReplaceRuntimeFrame(contest.Id, contest with { WuLuKangContest = receipt with { Replacements = receipt.Replacements.Select(p => p.FrameId == id ? paid : p).ToArray(),
                    Assignments = receipt.Assignments.Select(a => a.Seat == r.ParticipantSeat ? new WuLuKangPindianAssignment(a.Seat, card.Id, effective) : a).ToArray() } });
                AdvanceEventRulesAndQueueFact(new WuLuKangMaximumReservedEvent(id, r.ContestId, r.ParticipantSeat));
            }, emitCardMovedEvents: false, disclosure: CardMovementDisclosure.FaceDown);
            SealWuLuKangMovements(id, before, privateReserve: true); ContinueWuLuKangShenduan(id); return;
        }
        if (r.Stage == WuLuKangShenduanStage.ReserveChildren)
        {
            if (_winner != Winner.None || !WuLuKangInputsAvailable(parent)) { CancelWuLuKangFromShenduan(f, "reserved-input-unavailable"); return; }
            FinishWuLuKangShenduan(f); return;
        }
        if (_winner != Winner.None) { FinishWuLuKangShenduan(f); return; }
        if (r.Stage == WuLuKangShenduanStage.DrawChildren)
        { ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r = r with { Stage = WuLuKangShenduanStage.BottomChildren, RecipientIndex = r.RecipientIndex + 1, Draw = null, SelectedMinimumCardId = null, SelectedMinimumRank = null }, PendingMovementContinuation = null }); }
        while (r.RecipientIndex < r.Recipients.Count && !_players[r.Recipients[r.RecipientIndex]].IsAlive)
            ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r = r with { RecipientIndex = r.RecipientIndex + 1 } });
        if (r.RecipientIndex == r.Recipients.Count) { FinishWuLuKangShenduan(f); return; }
        var recipient = r.Recipients[r.RecipientIndex];
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.Drawing }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new WuLuKangKDrawIssuedEvent(id, r.ContestId, r.ComparisonIndex, r.Source, recipient));
        BeginOperationProgramDraw(f, recipient, 1, new(WuLuKangDrawReason));
    }
    private Card WuLuKangMinimumDrawMaterial(CharacterState player, CardMoveReason reason, IReadOnlyList<Card> stock, Card ordinary)
    {
        if (reason.Value != WuLuKangDrawReason || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangShenduan: { Stage: WuLuKangShenduanStage.Drawing } r,
            NativeDraw: { Operation: SkillProgramEffectOp.ResolveWuLuKangShenduan, RequestedCount: 1, Replacement: null } } f) return ordinary;
        if (!ValidWuLuKangShenduan(f) || r.Recipients[r.RecipientIndex] != player.Seat || r.SelectedMinimumCardId is not null || stock.Count == 0)
            throw new InvalidOperationException("Shenduan minimum material requires its one exact unreplaced native draw request.");
        var card = stock.Reverse().OrderBy(c => c.Rank).First();
        ReplaceRuntimeTop(f with { WuLuKangShenduan = r with { SelectedMinimumCardId = card.Id, SelectedMinimumRank = card.Rank } }); return card;
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuLuKangShenduan)]
    private void ReturnWuLuKangMinimumDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var r = f.WuLuKangShenduan!;
        if (!ValidWuLuKangShenduan(f) || r.Stage != WuLuKangShenduanStage.Drawing || outcome.RequestedCount != 1 || outcome.RecipientSeat != r.Recipients[r.RecipientIndex] ||
            outcome.ActualDrawIds.Count > 1 || outcome.ActualDrawIds.Count == 1 && outcome.ActualDrawIds[0] != r.SelectedMinimumCardId) throw new InvalidOperationException("Shenduan cannot replace its native minimum material or draw return.");
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuLuKangKDrawReturnedEvent(f.Id, outcome.RecipientSeat, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter));
        SealWuLuKangMovements(f.Id, outcome.SequenceBefore); ContinueWuLuKangShenduan(f.Id);
    }
    private void CancelWuLuKangFromShenduan(ProgramSkillFrame f, string reason)
    {
        var r = f.WuLuKangShenduan!; MarkWuLuKangCancelled(r.ContestId, reason); var parent = WuLuKangParent(r.ContestId); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = r with { Stage = WuLuKangShenduanStage.CancelChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        foreach (var id in WuLuKangHeldProcessing(parent)) MoveCard(GetAdvancedCard(id), CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        SealWuLuKangMovements(f.Id, before); ContinueWuLuKangShenduan(f.Id);
    }
    private void FinishWuLuKangShenduan(ProgramSkillFrame f)
    {
        var r = f.WuLuKangShenduan!; AdvanceEventRulesAndQueueFact(new WuLuKangShenduanCompletedEvent(f.Id, r.WindowId, r.ContestId, r.ComparisonIndex));
        ReplaceRuntimeTop(f = f with { WuLuKangShenduan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuLuKangShenduan)]
    private PromptChoice AiWuLuKangShenduan(PendingDecision pending, ProgramSkillFrame f) => pending.Choices.Where(c => c.Parameters["branch"] == "discard")
        .OrderBy(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[pending.PlayerSeat]) : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).FirstOrDefault() ?? pending.Choices[0];
}
