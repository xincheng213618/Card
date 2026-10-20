using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ExactMouGyzEndingParent(ProgramSkillFrame f) => f.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding } c &&
        _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is TurnEndingBoundaryFrame p && p.OwnerSeat == _currentSeat && p.TurnNumber == _turnNumber && c.SourceSeat == _currentSeat && c.TargetSeat == _currentSeat &&
        p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } candidate && candidate.OwnerSeat == f.OwnerSeat && candidate.SkillId == f.SkillId && candidate.SkillInstanceId == f.SkillInstanceId && candidate.BindingId == f.TriggerId && candidate.GameplayHash == f.GameplayHash;
    // Executor is the native instruction's actual discarder, not the original
    // card owner or the player who caused a replacement/ordinary Use cleanup.
    private IReadOnlyList<int> MouGyzDiscardCards(int actor, int turn) => ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>()
        .Where(e => e.TurnNumber == turn && e.ExecutorSeat == actor)
        .SelectMany(e => _cardMovements.Where(m => m.Sequence > e.SequenceBefore && m.Sequence <= e.SequenceAfter && m.To == CardLocation.DiscardPile &&
            GetProgramDiscardSource(m) is { } from && IsOwnedDiscardSource(from) &&
            (m.Reason == e.Reason || m.Reason.Value == e.Reason.Value + "-finished" || m.Reason.Value == e.Reason.Value + "-discard")))
        .Where(m => _cardZones.GetLocation(m.CardId) == CardLocation.DiscardPile).OrderBy(m => m.Sequence).Select(m => m.CardId).Distinct().ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32401, SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun)]
    private bool CanOfferMouGyzEnding(ProgramTriggerLegalityContext gate) => _players[_currentSeat].IsAlive &&
        (gate.WindowContext?.SourceSeat is not { } actor || actor == _currentSeat) && MouGyzDiscardCards(_currentSeat, _turnNumber).Count > 0;
    private void PayMouGyzObtain(ProgramSkillFrame f, int cardId)
    {
        var r = f.MouGuoYouZhi!;
        if (!ExactMouGyzEndingParent(f) || !MouGyzDiscardCards(r.ActorSeat, r.ActualTurn).Contains(cardId)) throw new InvalidOperationException("Zhongchun requires this actual Ending actor's own logical-discard entity.");
        ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.ObtainChildren, CardId = cardId, Paid = true, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(r.ActorSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiObtainIssuedEvent(f.Id, r.ActorSeat, cardId));
        MoveProgramCardsFromMultipleSources([cardId], CardLocation.Hand(r.ActorSeat), new(MouGyzObtainReason), disclosure: CardMovementDisclosure.FaceUp);
        SealMouGyz(f.Id); ContinueMouGyz(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouGuoYouZhiBique, SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun)]
    private PromptChoice ChooseMouGyzAi(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MouGuoYouZhi!; var actor = _players[p.PlayerSeat];
        if (r.Stage == MouGuoYouZhiStage.GiftChoice)
        {
            var candidates = p.Choices.Where(c => c.Cards.Count == 1 && !r.Selected.Contains(c.Cards[0])).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), actor)).ToArray();
            return r.Selected.Count < 3 && AreProgramDistributionAllies(actor, _players[r.ActorSeat]) && candidates.Length > 0 && GetKeepValue(GetAdvancedCard(candidates[0].Cards[0]), actor) < 5 ? candidates[0] : p.Choices.Single(c => c.Parameters["branch"] == "gift-confirm");
        }
        if (r.Stage == MouGuoYouZhiStage.AdjustChoice) return p.Choices.Single(c => c.Parameters["branch"] == (GetHand(actor).Count < r.DistinctNames ? "adjust" : "decline"));
        if (r.Stage == MouGuoYouZhiStage.AdjustDiscard)
        { var confirm = p.Choices.FirstOrDefault(c => c.Parameters["branch"] == "discard-confirm"); return confirm ?? p.Choices.Where(c => c.Cards.Count == 1 && !r.Selected.Contains(c.Cards[0])).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), actor)).First(); }
        if (r.Stage == MouGuoYouZhiStage.DiscardChoice)
            return AreProgramDistributionAllies(actor, _players[r.ActorSeat]) ? p.Choices.Where(c => c.Cards.Count == 1).OrderByDescending(c => GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[r.ActorSeat])).FirstOrDefault() ?? p.Choices.Last() : p.Choices.Last();
        if (r.Stage == MouGuoYouZhiStage.UseChoice)
        {
            var actions = MouGyzNativeActions(f);
            double Value(PromptChoice c)
            {
                if (c.Parameters["branch"] == "decline") return 0;
                var a = actions[int.Parse(c.Parameters["option"], CultureInfo.InvariantCulture)];
                var harmful = a.Kind is LegalActionKind.Slash or LegalActionKind.Duel or LegalActionKind.FireAttack or LegalActionKind.Snatch or LegalActionKind.Dismantlement or LegalActionKind.UnexpectedAssault or LegalActionKind.BorrowedSword;
                if (harmful) return a.TargetSeats.Any(s => AreProgramDistributionAllies(actor, _players[s])) ? -20 : 10;
                return 5;
            }
            return p.Choices.OrderByDescending(Value).First();
        }
        return p.Choices.Last();
    }
}
