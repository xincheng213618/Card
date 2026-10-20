using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouGyzGiftReason = "skill-program.mou-mobile:bique.give", MouGyzDrawReason = "skill-program.mou-mobile:bique.draw", MouGyzDiscardReason = "skill-program.mou-mobile:bique.discard", MouGyzObtainReason = "skill-program.mou-mobile:zhongchun.obtain";
    [ProgramCapability] private sealed class MouGuoYouZhiCapability(GameEngine g) : IMouGuoYouZhiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.BeginMouGyz(f, op); }
    private CardConversionSource MouGyzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private SkillProgramStepOutcome BeginMouGyz(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouGuoYouZhi is not null || f.InstructionIndex != 1) throw new InvalidOperationException("Guo You Zhi cannot repeat an accepted instruction.");
        var bique = op == SkillProgramEffectOp.ResolveMouGuoYouZhiBique;
        var actor = bique ? f.SelectedTargetSeats.Single() : f.WindowContext?.SourceSeat ?? _currentSeat;
        if (!IsValidPlayerSeat(actor) || !_players[actor].IsAlive || bique && (f.TriggerId is not null || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || actor == f.OwnerSeat) || !bique && !ExactMouGyzEndingParent(f)) throw new InvalidOperationException("Guo You Zhi lost its actual Play target or Ending actor.");
        var r = new MouGuoYouZhiReceipt(MouGyzSource(f), f.GameplayHash, op, actor, _turnNumber, _cardUseDebitPhaseInstanceId)
        { Stage = bique ? MouGuoYouZhiStage.GiftChoice : MouGuoYouZhiStage.DiscardChoice, WatchedHand = bique ? GetHand(_players[actor]).Select(c => c.Id).ToArray() : [], Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiStartedEvent(f.Id, r.Source, r.GameplayHash, op, actor, r.ActualTurn, r.ActualPhase));
        if (bique) AdvanceEventRulesAndQueueFact(new MouGuoYouZhiViewedEvent(f.Id, f.OwnerSeat, actor, r.WatchedHand.Count));
        PublishMouGyz(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MouGyzChoice(ProgramSkillFrame f, string branch, string text, int? card = null, int? option = null) =>
        new(new($"mou-guo-you-zhi.{f.Id}.{branch}.{card ?? option ?? -1}"), text, card is { } id ? [id] : [], [], new Dictionary<string, string> { ["program-action"] = "mou-guo-you-zhi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["option"] = (option ?? -1).ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<Card> MouGyzEligibleGifts(ProgramSkillFrame f)
    {
        var r = f.MouGuoYouZhi!; var names = GetHand(_players[r.ActorSeat]).Select(c => AdvancedEffectiveHandKind(_players[r.ActorSeat], c)).ToHashSet();
        return GetHand(_players[f.OwnerSeat]).Where(c => !names.Contains(AdvancedEffectiveHandKind(_players[f.OwnerSeat], c))).ToArray();
    }
    private IReadOnlyList<PromptChoice> MouGyzChoices(ProgramSkillFrame f)
    {
        var r = f.MouGuoYouZhi!; var list = new List<PromptChoice>();
        if (r.Stage == MouGuoYouZhiStage.GiftChoice)
        {
            list.AddRange(MouGyzEligibleGifts(f).Select(c => MouGyzChoice(f, "gift-card", (r.Selected.Contains(c.Id) ? "取消：" : "交给：") + c.DisplayName, c.Id)));
            list.Add(MouGyzChoice(f, "gift-confirm", $"确认交给{r.Selected.Count}张牌"));
        }
        else if (r.Stage == MouGuoYouZhiStage.AdjustChoice)
        { list.Add(MouGyzChoice(f, "adjust", $"将手牌数调整至{r.DistinctNames}")); list.Add(MouGyzChoice(f, "decline", "不调整手牌")); }
        else if (r.Stage == MouGuoYouZhiStage.AdjustDiscard)
        {
            list.AddRange(GetHand(_players[f.OwnerSeat]).Select(c => MouGyzChoice(f, "discard-card", (r.Selected.Contains(c.Id) ? "取消：" : "弃置：") + c.DisplayName, c.Id)));
            if (r.Selected.Count == GetHand(_players[f.OwnerSeat]).Count - r.DistinctNames) list.Add(MouGyzChoice(f, "discard-confirm", $"确认弃置{r.Selected.Count}张手牌"));
        }
        else if (r.Stage == MouGuoYouZhiStage.DiscardChoice)
        { list.AddRange(MouGyzDiscardCards(r.ActorSeat, r.ActualTurn).Select(id => MouGyzChoice(f, "obtain", $"令{_players[r.ActorSeat].Name}获得【{GetAdvancedCard(id).DisplayName}】", id))); list.Add(MouGyzChoice(f, "decline", "不发动忠纯")); }
        else if (r.Stage == MouGuoYouZhiStage.UseChoice)
        {
            list.AddRange(MouGyzNativeActions(f).Select((a, i) => new PromptChoice(new($"mou-guo-you-zhi.{f.Id}.use.{i}"), a.Description, [r.CardId], a.TargetSeats, new Dictionary<string, string> { ["program-action"] = "mou-guo-you-zhi", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "use", ["option"] = i.ToString(CultureInfo.InvariantCulture) })));
            list.Add(MouGyzChoice(f, "decline", "不使用此牌"));
        }
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishMouGyz(ProgramSkillFrame f)
    {
        var r = f.MouGuoYouZhi!; var chooser = r.Stage == MouGuoYouZhiStage.UseChoice ? r.ActorSeat : f.OwnerSeat;
        if (_winner != Winner.None || !_players[chooser].IsAlive || r.Stage == MouGuoYouZhiStage.GiftChoice && !_players[r.ActorSeat].IsAlive) { FinishMouGyz(f); return; }
        var choices = MouGyzChoices(f); if (choices.Count == 0 || r.Stage == MouGuoYouZhiStage.UseChoice && choices.Count == 1) { FinishMouGyz(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouGuoYouZhiStage.GiftChoice ? "裨阙：已观看对方手牌，选择交出的手牌（可为零张）。" : r.Stage == MouGuoYouZhiStage.UseChoice ? "忠纯：可以使用刚获得的原牌，或不使用。" : skill.Description,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, TargetSeat = r.ActorSeat, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-guo-you-zhi")]
    private void AnswerMouGyz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoYouZhi: { } r } f || !ValidMouGyz(f) || _pendingDecision?.PlayerSeat != (r.Stage == MouGuoYouZhiStage.UseChoice ? r.ActorSeat : f.OwnerSeat) || !MouGyzChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Guo You Zhi requires its actual chooser and unchanged published choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch is "gift-card" or "discard-card")
        { var id = choice.Cards.Single(); ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Selected = r.Selected.Contains(id) ? r.Selected.Where(c => c != id).ToArray() : r.Selected.Append(id).ToArray() } }); PublishMouGyz(f); return; }
        if (branch == "decline") { if (r.Stage == MouGuoYouZhiStage.AdjustChoice) AdvanceEventRulesAndQueueFact(new MouGuoYouZhiAdjustChosenEvent(f.Id, r.DistinctNames, GetHand(_players[f.OwnerSeat]).Count, false)); FinishMouGyz(f); return; }
        if (branch == "gift-confirm") { PayMouGyzGift(f); return; }
        if (branch == "adjust")
        {
            var count = GetHand(_players[f.OwnerSeat]).Count; AdvanceEventRulesAndQueueFact(new MouGuoYouZhiAdjustChosenEvent(f.Id, r.DistinctNames, count, true));
            if (count < r.DistinctNames)
            { ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.Drawing, Requested = r.DistinctNames - count } }); AdvanceEventRulesAndQueueFact(new MouGuoYouZhiDrawIssuedEvent(f.Id, f.OwnerSeat, r.DistinctNames - count)); BeginOperationProgramDraw(f, f.OwnerSeat, r.DistinctNames - count, new(MouGyzDrawReason)); return; }
            if (count == r.DistinctNames) { FinishMouGyz(f); return; }
            ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.AdjustDiscard, Selected = [] } }); PublishMouGyz(f); return;
        }
        if (branch == "discard-confirm")
        {
            if (r.Selected.Count != GetHand(_players[f.OwnerSeat]).Count - r.DistinctNames || r.Selected.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat))) throw new InvalidOperationException("Bique must really discard the excess Hand.");
            ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.DiscardChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Selected, new(MouGyzDiscardReason)); SealMouGyz(f.Id); ContinueMouGyz(f.Id); return;
        }
        if (branch == "obtain") { PayMouGyzObtain(f, choice.Cards.Single()); return; }
        if (branch == "use") { IssueMouGyzNativeUse(f, int.Parse(choice.Parameters["option"], CultureInfo.InvariantCulture)); return; }
        throw new InvalidOperationException("Unknown Guo You Zhi branch.");
    }
    private void PayMouGyzGift(ProgramSkillFrame f)
    {
        var r = f.MouGuoYouZhi!; var allowed = MouGyzEligibleGifts(f).Select(c => c.Id).ToHashSet();
        if (r.Selected.Any(id => !allowed.Contains(id)) || r.Selected.Distinct().Count() != r.Selected.Count) throw new InvalidOperationException("Bique cannot give a newly owned target name or another location.");
        var gifts = r.Selected.Select(id => new MouGuoYouZhiGift(id, AdvancedEffectiveHandKind(_players[f.OwnerSeat], GetAdvancedCard(id)))).ToArray();
        ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.GiftChildren, Gifts = gifts, Paid = true, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(r.ActorSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiGiftAcceptedEvent(f.Id, r.ActorSeat, gifts.Length));
        if (gifts.Length > 0) MoveProgramCardsFromMultipleSources(gifts.Select(g => g.CardId).ToArray(), CardLocation.Hand(r.ActorSeat), new(MouGyzGiftReason), disclosure: CardMovementDisclosure.FaceDown);
        SealMouGyz(f.Id); f = GetActiveProgramFrame(f.Id); r = f.MouGuoYouZhi!;
        var actual = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After && m.To == CardLocation.Hand(r.ActorSeat) && m.Reason.Value == MouGyzGiftReason && gifts.Any(g => g.CardId == m.CardId)).Select(m => m.CardId).ToHashSet();
        var names = gifts.Where(g => actual.Contains(g.CardId)).Select(g => g.EffectiveKind).Distinct().Count();
        ReplaceRuntimeTop(f with { MouGuoYouZhi = r with { DistinctNames = names, WatchedHand = [], Selected = [] } });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiGiftReturnedEvent(f.Id, actual.Count, names)); ContinueMouGyz(f.Id);
    }
    private void ContinueMouGyz(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.MouGuoYouZhi!;
        if (r.Stage is MouGuoYouZhiStage.Drawing or MouGuoYouZhiStage.IssuingUse or MouGuoYouZhiStage.NativeUse) return;
        if (r.Stage is MouGuoYouZhiStage.GiftChildren or MouGuoYouZhiStage.DrawChildren or MouGuoYouZhiStage.DiscardChildren or MouGuoYouZhiStage.ObtainChildren)
        {
            if (DrainMouGyz(f)) return; f = GetActiveProgramFrame(id); r = f.MouGuoYouZhi!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (_winner != Winner.None) { FinishMouGyz(f); return; }
            if (r.Stage == MouGuoYouZhiStage.GiftChildren)
            { if (!_players[f.OwnerSeat].IsAlive || GetHand(_players[f.OwnerSeat]).Count == r.DistinctNames) { FinishMouGyz(f); return; } ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.AdjustChoice } }); PublishMouGyz(f); return; }
            if (r.Stage == MouGuoYouZhiStage.ObtainChildren)
            { if (!_players[r.ActorSeat].IsAlive || _cardZones.GetLocation(r.CardId) != CardLocation.Hand(r.ActorSeat)) { FinishMouGyz(f); return; } ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.UseChoice } }); PublishMouGyz(f); return; }
            FinishMouGyz(f); return;
        }
        if (r.Stage == MouGuoYouZhiStage.Returned) { FinishMouGyz(f); return; }
        if (_pendingDecision is null) PublishMouGyz(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouGuoYouZhiBique)]
    private void ReturnMouGyzDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouGuoYouZhi is not { Stage: MouGuoYouZhiStage.Drawing, Draw: null } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Requested) throw new InvalidOperationException("Bique draw must return its real Hand normalization.");
        ReplaceRuntimeTop(f = f with { MouGuoYouZhi = r with { Stage = MouGuoYouZhiStage.DrawChildren, Draw = o, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouGuoYouZhiDrawReturnedEvent(f.Id, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.AttemptFrameId)); SealMouGyz(f.Id); ContinueMouGyz(f.Id);
    }
    private void FinishMouGyz(ProgramSkillFrame f)
    { var r = f.MouGuoYouZhi!; AdvanceEventRulesAndQueueFact(new MouGuoYouZhiCompletedEvent(f.Id, r.Op, r.DistinctNames, r.Native?.UseId)); ReplaceRuntimeTop(f = f with { MouGuoYouZhi = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 32400)]
    private CardSnapshot[] MouGyzPrivateCards(int viewer) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewer && f.MouGuoYouZhi is { Stage: MouGuoYouZhiStage.GiftChoice }).SelectMany(f => f.MouGuoYouZhi!.WatchedHand.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(f.MouGuoYouZhi.ActorSeat)).Select(id => ProjectHandCard(_players[f.MouGuoYouZhi.ActorSeat], GetAdvancedCard(id)))).ToArray();
}
