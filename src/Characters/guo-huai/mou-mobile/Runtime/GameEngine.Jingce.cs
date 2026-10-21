using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouGhStore = "skill-program.mou-mobile:jingce.store", MouGhPlace = "skill-program.mou-mobile:jingce.place", MouGhDraw = "skill-program.mou-mobile:jingce.draw";
    [ProgramCapability] private sealed class MouGuoHuaiCapability(GameEngine g) : IMouGuoHuaiMobileHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => g.BeginMouGh(f, op); }
    private SkillProgramStepOutcome BeginMouGh(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id); var c = f.WindowContext ?? throw new InvalidOperationException("Jingce requires its original native trigger.");
        if (f.MouGuoHuai is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) throw new InvalidOperationException("Jingce cannot issue its original instruction twice.");
        var r = new MouGuoHuaiReceipt(MouGhSource(f), f.GameplayHash, op, c.ParentFrameId, _turnNumber);
        if (op == SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart)
        {
            if (_resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramLifecycleTriggerWindowFrame { Continuation: ProgramLifecycleContinuation.MouGuoHuaiActualStart } || c.SourceSeat != f.OwnerSeat) throw new InvalidOperationException("Jingce start requires its real pre-round actual-start window.");
            var rewards = ProgramEventHistory<MouGuoHuaiPredictionDueEvent>().Where(d => d.ActualTurn == _turnNumber && d.NoneSuccess && ProgramEventHistory<MouGuoHuaiPredictionIssuedEvent>().Any(p => p.PredictionId == d.PredictionId && p.Source.OwnerSeat == f.OwnerSeat && p.Source.SkillInstanceId == f.SkillInstanceId && p.GameplayHash == f.GameplayHash) && !ProgramEventHistory<MouGuoHuaiPredictionConsumedEvent>().Any(e => e.PredictionId == d.PredictionId)).Select(d => d.PredictionId).ToArray();
            r = r with { Stage = MouGuoHuaiStage.StartRewards, Rewards = rewards, Paid = true };
        }
        else if (op == SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction)
        {
            var pile = PublicPileSources(f.OwnerSeat).SingleOrDefault(s => s.SkillId == f.SkillId && s.SkillInstanceId == f.SkillInstanceId);
            if (pile is null || PublicPileCards(pile).Count == 0) return SkillProgramStepOutcome.Continue;
            r = r with { Stage = MouGuoHuaiStage.SelectingCard, Cards = PublicPileCards(pile).Select(card => card.Id).ToArray() };
        }
        else
        {
            var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex);
            var q = MouGuoHuaiGainQualification(candidate, c) ?? throw new InvalidOperationException("Jingce gain reward lost the first actual Hand gain.");
            if (ProgramEventHistory<MouGuoHuaiPredictionConsumedEvent>().Any(e => e.PredictionId == q.PredictionId)) return SkillProgramStepOutcome.Continue;
            r = r with { Stage = MouGuoHuaiStage.StartRewards, Rewards = [q.PredictionId], Paid = true };
        }
        ReplaceRuntimeTop(f = f with { MouGuoHuai = r }); AdvanceEventRulesAndQueueFact(new MouGuoHuaiStartedEvent(f.Id, r.Source, r.GameplayHash, op, r.ParentId, r.ActualTurn));
        if (r.Stage == MouGuoHuaiStage.SelectingCard) PublishMouGh(f); else ContinueMouGh(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MouGhChoice(ProgramSkillFrame f, string branch, string description, int? card = null, int? target = null) => new(new($"mou-guo-huai.{f.Id}.{branch}.{card ?? target ?? -1}"), description, card is { } id ? [id] : [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "mou-guo-huai", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouGhChoices(ProgramSkillFrame f)
    {
        var r = f.MouGuoHuai!; var list = new List<PromptChoice>();
        if (r.Stage == MouGuoHuaiStage.SelectingCard)
            foreach (var id in r.Cards.Where(id => r.Predictions.All(p => p.CardId != id))) list.Add(MouGhChoice(f, "card", $"选择第{r.Predictions.Count + 1}张预测：【{GetAdvancedCard(id).DisplayName}】", card: id));
        if (r.Stage == MouGuoHuaiStage.SelectingRecipient)
        {
            list.AddRange(_players.Where(p => p.IsAlive).Select(p => MouGhChoice(f, "recipient", $"预测{p.Name}首次获得此牌", target: p.Seat)));
            list.Add(MouGhChoice(f, "none", "预测到本人下次回合开始仍无人获得此牌"));
        }
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishMouGh(ProgramSkillFrame f)
    {
        var choices = MouGhChoices(f); if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishMouGh(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "精策：选择预测顺序及此牌的首次获得者。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat) { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-guo-huai")]
    private void AnswerMouGh(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoHuai: { } r } f || !ValidMouGh(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat || !MouGhChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Jingce prediction changed its real owning frame or public original entity.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (branch == "card") { ReplaceRuntimeTop(f = f with { MouGuoHuai = r with { SelectedCard = choice.Cards.Single(), Stage = MouGuoHuaiStage.SelectingRecipient } }); PublishMouGh(f); return; }
        if (branch is not ("none" or "recipient")) throw new InvalidOperationException("Unknown Jingce prediction.");
        var p = new MouGuoHuaiPrediction(r.SelectedCard, r.Predictions.Count + 1, branch == "none" ? null : choice.Targets.Single());
        ReplaceRuntimeTop(f = f with { MouGuoHuai = r = r with { Predictions = r.Predictions.Append(p).ToArray(), SelectedCard = 0, Stage = MouGuoHuaiStage.SelectingCard } });
        if (r.Predictions.Count < r.Cards.Count) { PublishMouGh(f); return; }
        PlaceMouGhPredictions(f);
    }
    private void PlaceMouGhPredictions(ProgramSkillFrame f)
    {
        var r = f.MouGuoHuai!; var pile = PublicPileSources(f.OwnerSeat).Single(s => s.SkillId == f.SkillId && s.SkillInstanceId == f.SkillInstanceId);
        if (r.Predictions.Any(p => _cardZones.GetLocation(p.CardId) != pile.Location)) throw new InvalidOperationException("Jingce cannot place a missing physical prediction.");
        var oldTopFirst = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c => c.Id).ToArray();
        ReplaceRuntimeTop(f = f with { MouGuoHuai = r with { Stage = MouGuoHuaiStage.PlacingChildren, Paid = true, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(r.Predictions.Select(p => p.CardId).ToArray(), CardLocation.DrawPile, new(MouGhPlace), (_, moves) =>
        {
            var current = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c => c.Id).ToArray(); var order = oldTopFirst.ToList();
            foreach (var p in r.Predictions)
            {
                var moved = moves.Single(m => m.CardId == p.CardId && m.From == pile.Location && m.To == CardLocation.DrawPile);
                var position = p.Ordinal * 3; var bottom = position > order.Count + 1; if (bottom) order.Add(p.CardId); else order.Insert(position - 1, p.CardId);
                var predictionId = ++_resolutionSequence;
                AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionIssuedEvent(predictionId, f.Id, r.Source, r.GameplayHash, r.ActualTurn, p.CardId, p.Ordinal, p.RecipientSeat));
                AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionPlacedEvent(predictionId, p.CardId, p.Ordinal, order.IndexOf(p.CardId) + 1, bottom, moved.From, moved.To, moved.Sequence));
            }
            _cardZones.ReorderDrawPileTop(current, order, []);
        }, disclosure: CardMovementDisclosure.FaceUp);
        SealMouGh(f.Id); ContinueMouGh(f.Id);
    }
    private void ContinueMouGh(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouGuoHuai: { } r } f || f.Id != id) return;
        if (!ValidMouGh(f)) throw new InvalidOperationException("Jingce lost its actual prediction or paid native return.");
        if (r.Stage is MouGuoHuaiStage.Drawing or MouGuoHuaiStage.SelectingCard or MouGuoHuaiStage.SelectingRecipient) return;
        if (r.Stage is MouGuoHuaiStage.StoreChildren or MouGuoHuaiStage.PlacingChildren or MouGuoHuaiStage.DrawChildren)
        {
            if (DrainMouGh(f)) return; f = GetActiveProgramFrame(id); r = f.MouGuoHuai!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage != MouGuoHuaiStage.DrawChildren || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishMouGh(f); return; }
            ReplaceRuntimeTop(f = f with { MouGuoHuai = r = r with { Index = r.Index + 1, Stage = MouGuoHuaiStage.StartRewards, Draw = null } });
        }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishMouGh(f); return; }
        if (r.Index < r.Rewards.Count)
        {
            var predictionId = r.Rewards[r.Index]; var p = ProgramEventHistory<MouGuoHuaiPredictionIssuedEvent>().Single(p => p.PredictionId == predictionId);
            if (ProgramEventHistory<MouGuoHuaiPredictionConsumedEvent>().Any(e => e.PredictionId == predictionId)) throw new InvalidOperationException("A prediction reward cannot be issued twice.");
            ReplaceRuntimeTop(f = f with { MouGuoHuai = r with { Stage = MouGuoHuaiStage.Drawing, PredictionId = predictionId, Requested = p.Ordinal, Paid = true } });
            AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionConsumedEvent(predictionId, f.Id, r.Op == SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart ? "none-at-next-start" : "first-hand-gain"));
            AdvanceEventRulesAndQueueFact(new MouGuoHuaiRewardIssuedEvent(f.Id, predictionId, p.Ordinal, f.OwnerSeat)); BeginOperationProgramDraw(f, f.OwnerSeat, p.Ordinal, new(MouGhDraw)); return;
        }
        if (r.Op != SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart) { FinishMouGh(f); return; }
        foreach (var due in ProgramEventHistory<MouGuoHuaiPredictionDueEvent>().Where(d => d.ActualTurn == r.ActualTurn && ProgramEventHistory<MouGuoHuaiPredictionIssuedEvent>().Any(p => p.PredictionId == d.PredictionId && p.Source.OwnerSeat == f.OwnerSeat && p.Source.SkillInstanceId == f.SkillInstanceId) && !ProgramEventHistory<MouGuoHuaiPredictionConsumedEvent>().Any(e => e.PredictionId == d.PredictionId)).ToArray())
            AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionConsumedEvent(due.PredictionId, f.Id, "clear-unverified-at-next-start"));
        if (PublicPileSources(f.OwnerSeat).Any(s => s.SkillId == f.SkillId && PublicPileCards(s).Count != 0)) { FinishMouGh(f); return; }
        StoreMouGhTop(f);
    }
    private void StoreMouGhTop(ProgramSkillFrame f)
    {
        var r = f.MouGuoHuai!; var pile = EnsurePublicPileSource(f, 3);
        ReplaceRuntimeTop(f = f with { MouGuoHuai = r with { Stage = MouGuoHuaiStage.StoreChildren, Before = _movementSequence, After = _movementSequence, Paid = true }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        // Native stock refill is retained, but these are placements, never draws.
        for (var i = 0; i < 3 && EnsureDrawPile(); i++)
        {
            var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1]; var ordinal = i;
            MoveCard(card, CardLocation.DrawPile, pile.Location, new(MouGhStore), m => AdvanceEventRulesAndQueueFact(new MouGuoHuaiStoredEvent(f.Id, r.Source, ordinal, m.CardId, m.From, m.To, m.Sequence)), disclosure: CardMovementDisclosure.FaceUp);
        }
        SealMouGh(f.Id); ContinueMouGh(f.Id);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart, SkillProgramEffectOp.ResolveMouGuoHuaiJingceReward)]
    private void ReturnMouGhDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MouGuoHuai is not { Stage: MouGuoHuaiStage.Drawing, Draw: null } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.Requested) throw new InvalidOperationException("Jingce lost its exact issued native reward.");
        ReplaceRuntimeTop(f = f with { MouGuoHuai = r with { Stage = MouGuoHuaiStage.DrawChildren, Draw = o, Before = o.SequenceBefore, After = o.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouGuoHuaiDrawReturnedEvent(f.Id, r.PredictionId, o.RequestedCount, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.AttemptFrameId)); SealMouGh(f.Id); ContinueMouGh(f.Id);
    }
    private void FinishMouGh(ProgramSkillFrame f)
    {
        var r = f.MouGuoHuai!; AdvanceEventRulesAndQueueFact(new MouGuoHuaiCompletedEvent(f.Id, r.Op, r.Predictions.Count, r.Index));
        ReplaceRuntimeTop(f = f with { MouGuoHuai = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction)]
    private PromptChoice ChooseMouGhAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.MouGuoHuai!.Stage == MouGuoHuaiStage.SelectingCard) return p.Choices.First();
        // Public turn order and normal draw count only; unknown stock is not read.
        var ordinal = f.MouGuoHuai.Predictions.Count + 1; var steps = Math.Clamp((ordinal * 3 + 1) / 2, 1, _players.Count);
        var alive = _players.Where(x => x.IsAlive).OrderBy(x => RelativeTableOrder(f.OwnerSeat, x.Seat)).ToArray();
        var predicted = alive[steps % alive.Length].Seat;
        return p.Choices.FirstOrDefault(c => c.Targets.SequenceEqual([predicted])) ?? p.Choices.Last();
    }
}
