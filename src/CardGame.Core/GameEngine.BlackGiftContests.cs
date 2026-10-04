using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource RecipientConsequencesSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static string BlackGiftReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest}.gift";
    private static string BlackGiftDiscardReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest}.discard";
    private bool BlackGiftMaterialLegal(int owner, Card card) => _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner) &&
        EffectiveSuit(_players[owner], card) is Suit.Spade or Suit.Club;

    private SkillProgramStepOutcome BeginBlackGiftContest(ProgramSkillFrame supplied, int recipient)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.BlackGiftContest is not null || f.InstructionIndex != 1 || f.TriggerId is not null || f.WindowContext is not null ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.SelectedTargetSeats is not [var selected] || selected != recipient ||
            recipient == f.OwnerSeat || !IsValidPlayerSeat(recipient) || f.SelectedCardIds is not [var id])
            throw new InvalidOperationException("A black gift contest lost its exact first other recipient/material.");
        var card = GetAdvancedCard(id);
        if (!_players[f.OwnerSeat].IsAlive || !_players[recipient].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !BlackGiftMaterialLegal(f.OwnerSeat, card))
        { CancelProgramBindingAndCleanup(f, "赠牌前来源、黑色手牌或接收者失效。"); return SkillProgramStepOutcome.AwaitChild; }
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        var receipt = new BlackGiftContestReceipt(1, RecipientConsequencesSource(f), f.GameplayHash, _turnNumber, _currentSeat,
            recipient, id, EffectiveSuit(_players[f.OwnerSeat], card), before, before);
        ReplaceRuntimeTop(f with { BlackGiftContest = receipt, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(recipient), new(BlackGiftReason(f)), (_, records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = receipt with { GiftAfter = records.Single().Sequence };
            ReplaceRuntimeTop(active with { BlackGiftContest = paid });
            AdvanceEventRulesAndQueueFact(new BlackGiftContestGiftPaidEvent(f.Id, paid.Source, paid.GameplayHash,
                paid.ActualTurn, paid.TurnOwnerSeat, recipient, before, paid.GiftAfter));
        });
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private int[] BlackGiftSecondSeats(ProgramSkillFrame f)
    {
        var first = f.BlackGiftContest!.RecipientSeat;
        if (!_players[first].IsAlive || GetHand(_players[first]).Count == 0) return [];
        // “另一名角色” may be the original owner; its hand is checked after the gift.
        return _players.Where(p => p.IsAlive && p.Seat != first && CanBePindianTarget(first, p.Seat)).Select(p => p.Seat).ToArray();
    }
    private IReadOnlyList<PromptChoice> BlackGiftDiscardOptions(ProgramSkillFrame f) =>
        BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, BlackGiftWinner(f.BlackGiftContest!.Result!)!.Value,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
            canSelect: (_, c) => !f.BlackGiftContest.SelectedDiscardIds.Contains(c.Id));
    private static int? BlackGiftWinner(PindianResult p) => p.SourceRank == p.OpponentRank ? null : p.SourceWon ? p.SourceSeat : p.OpponentSeat;
    private static int[] BlackGiftLosers(PindianResult p) => new[] { p.SourceSeat, p.OpponentSeat }.Where(s => s != BlackGiftWinner(p)).ToArray();

    private IReadOnlyList<PromptChoice> BlackGiftChoices(ProgramSkillFrame f)
    {
        var r = f.BlackGiftContest!;
        return Array.AsReadOnly((r.Stage == BlackGiftContestStage.ChoosingSecond
            ? BlackGiftSecondSeats(f).Select(seat => new PromptChoice(new($"black-gift.{f.Id}.second-{seat}"),
                $"令 {_players[r.RecipientSeat].Name} 与 {_players[seat].Name} 拼点", [], [seat], BlackGiftParameters(f, "second")))
            : BlackGiftDiscardOptions(f).Select(c => new PromptChoice(new($"black-gift.{f.Id}.discard-{c.Cards.Single()}"), c.Label,
                c.Cards, [], BlackGiftParameters(f, "discard")))).ToArray());
    }
    private static Dictionary<string, string> BlackGiftParameters(ProgramSkillFrame f, string stage) => new()
    { ["program-action"] = "black-gift-contest", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["stage"] = stage };
    private void PublishBlackGiftChoice(ProgramSkillFrame f)
    {
        if (RecipientConsequencesGameEnded()) return;
        var r = f.BlackGiftContest!; var choices = BlackGiftChoices(f);
        if (choices.Count == 0) { FinishProgramSkill(f, true); return; }
        var seat = r.Stage == BlackGiftContestStage.ChoosingSecond ? f.OwnerSeat : BlackGiftWinner(r.Result!)!.Value;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat,
            r.Stage == BlackGiftContestStage.ChoosingSecond ? "选择受赠者的另一名拼点角色。" : "请选择赢家必须实际弃置的牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveBlackGiftChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertRecipientConsequences(f); var r = f.BlackGiftContest!;
        var seat = r.Stage == BlackGiftContestStage.ChoosingSecond ? f.OwnerSeat : BlackGiftWinner(r.Result!)!.Value;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != seat ||
            !AssistedChoicesEqual([choice], [BlackGiftChoices(f).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("A black gift answer lost its exact private owning choice.");
        ClearPendingDecision();
        if (r.Stage == BlackGiftContestStage.ChoosingSecond)
        {
            var second = choice.Targets.Single();
            if (!BlackGiftSecondSeats(f).Contains(second)) { FinishProgramSkill(f, true); return; }
            var pindian = _resolutionSequence + 1;
            ReplaceRuntimeTop(f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.Pindian, SecondSeat = second, PindianFrameId = pindian },
                PendingMovementContinuation = new(r.RecipientSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new BlackGiftContestStartedEvent(f.Id, r.RecipientSeat, second, pindian));
            var skill = _contentRegistry.GetSkill(f.SkillId);
            BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name, skill.Description), r.RecipientSeat, second,
                programResultBind: BlackGiftResultBind(f), programResultVisibility: SkillProgramCardSetVisibility.Public);
            return;
        }
        if (r.Stage != BlackGiftContestStage.ChoosingDiscard) throw new InvalidOperationException("A completed cost cannot be selected twice.");
        var selected = r.SelectedDiscardIds.Append(choice.Cards.Single()).ToArray();
        ReplaceRuntimeTop(f = f with { BlackGiftContest = r = r with { SelectedDiscardIds = selected } });
        if (selected.Length < r.RequiredDiscards) { PublishBlackGiftChoice(f); return; }
        var winner = BlackGiftWinner(r.Result!)!.Value;
        var locations = selected.Select(_cardZones.GetLocation).ToArray();
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        var invoice = new BlackGiftContestDiscard(winner, selected, locations, before, before);
        ReplaceRuntimeTop(f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.DiscardPaid, SelectedDiscardIds = [], Discard = invoice },
            PendingMovementContinuation = new(winner, 0, null) });
        MoveProgramCardsFromMultipleSources(selected, CardLocation.DiscardPile, new(BlackGiftDiscardReason(f)), (_, records) =>
        {
            var active = GetActiveProgramFrame(f.Id); var paid = invoice with { After = records.Max(m => m.Sequence) };
            ReplaceRuntimeTop(active with { BlackGiftContest = active.BlackGiftContest! with { Discard = paid } });
            AdvanceEventRulesAndQueueFact(new BlackGiftContestDiscardPaidEvent(f.Id, winner, selected.Length, before, paid.After));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private static string BlackGiftResultBind(ProgramSkillFrame f) => $"black-gift-result-{f.Id}";
    private bool BlackGiftResultMatches(ProgramSkillFrame f, out PindianResult result)
    {
        result = null!; var r = f.BlackGiftContest!;
        if (r.PindianFrameId is not { } id || r.SecondSeat is not { } second ||
            f.PindianResultBindings.SingleOrDefault(b => b.Name == BlackGiftResultBind(f)) is not { } b) return false;
        var facts = CompleteProgramEventHistory().OfType<PindianResultDeterminedEvent>().Where(e => e.FrameId == id && e.SkillId == f.SkillId).ToArray();
        if (facts is not [var fact] || fact.Result.SourceSeat != r.RecipientSeat || fact.Result.OpponentSeat != second ||
            b.SourceSeat != r.RecipientSeat || b.OpponentSeat != second || b.SourceRank != fact.Result.SourceRank ||
            b.OpponentRank != fact.Result.OpponentRank || b.SourceWon != fact.Result.SourceWon || b.Visibility != SkillProgramCardSetVisibility.Public) return false;
        result = fact.Result; return true;
    }

    private bool DrainRecipientConsequences(ProgramSkillFrame f, bool movement) =>
        TryBeginQueuedRecoveryReplacement(f.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, movement ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private bool ResumeBlackGiftContest(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.BlackGiftContest is not { } r) return false;
        AssertRecipientConsequences(f);
        if (RecipientConsequencesGameEnded()) return true;
        if (r.Stage is BlackGiftContestStage.ChoosingSecond or BlackGiftContestStage.ChoosingDiscard)
        { if (_pendingDecision is null) PublishBlackGiftChoice(f); return true; }
        if (DrainRecipientConsequences(f, f.PendingMovementContinuation is not null)) return true;
        f = GetActiveProgramFrame(id); r = f.BlackGiftContest!;
        if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == BlackGiftContestStage.GiftPaid)
        {
            ReplaceRuntimeTop(f = f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.ChoosingSecond } });
            PublishBlackGiftChoice(f); return true;
        }
        if (r.Stage == BlackGiftContestStage.Pindian)
        {
            if (!BlackGiftResultMatches(f, out var result)) throw new InvalidOperationException("Black gift requires its completed real Pindian result.");
            AdvanceEventRulesAndQueueFact(new BlackGiftContestResultCapturedEvent(id, r.PindianFrameId!.Value, result));
            ReplaceRuntimeTop(f = f with { BlackGiftContest = r = r with { Result = result, Stage = BlackGiftContestStage.LossReady } });
            if (BlackGiftWinner(result) is { } winner && _players[winner].IsAlive)
            {
                var count = Math.Min(2, BlackGiftDiscardOptions(f).Count);
                if (count > 0)
                { ReplaceRuntimeTop(f = f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.ChoosingDiscard, RequiredDiscards = count } }); PublishBlackGiftChoice(f); return true; }
            }
        }
        else if (r.Stage == BlackGiftContestStage.DiscardPaid)
            ReplaceRuntimeTop(f = f with { BlackGiftContest = r = r with { Stage = BlackGiftContestStage.LossReady } });
        else if (r.Stage == BlackGiftContestStage.LossPaid)
            ReplaceRuntimeTop(f = f with { BlackGiftContest = r = r with { Stage = BlackGiftContestStage.LossReady, LossIndex = r.LossIndex + 1 } });
        if (r.Stage == BlackGiftContestStage.LossReady)
        {
            var losers = BlackGiftLosers(r.Result!);
            while (r.LossIndex < losers.Length && !_players[losers[r.LossIndex]].IsAlive)
                ReplaceRuntimeTop(f = f with { BlackGiftContest = r = r with { LossIndex = r.LossIndex + 1 } });
            if (r.LossIndex < losers.Length)
            {
                var seat = losers[r.LossIndex]; var before = _players[seat].Hp;
                var loss = new BlackGiftContestLoss(seat, before, Math.Max(0, before - 1));
                ReplaceRuntimeTop(f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.LossPaid, Losses = r.Losses.Append(loss).ToArray() } });
                AdvanceEventRulesAndQueueFact(new BlackGiftContestLossPaidEvent(id, r.LossIndex, loss));
                var outcome = new ProgramSkillHost(this).LoseHp(id, f.SkillId, seat, 1);
                if (outcome == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(id);
                return true;
            }
            ReplaceRuntimeTop(f = f with { BlackGiftContest = r with { Stage = BlackGiftContestStage.Complete } });
        }
        FinishProgramSkill(f, true); return true;
    }

    private sealed partial class ProgramSkillHost : IRecipientContestConsequencesHost
    {
        public SkillProgramStepOutcome BlackGiftContest(ProgramSkillFrame f, int recipient) => engine.BeginBlackGiftContest(f, recipient);
        public SkillProgramStepOutcome PrintedLordBenefit(ProgramSkillFrame f) => engine.BeginPrintedLordBenefit(f);
    }
}
