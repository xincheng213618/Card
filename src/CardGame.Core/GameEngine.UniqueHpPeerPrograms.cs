using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardConversionSource RecipientContestSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private long RecipientContestSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private bool RecipientContestUnissuedSource(ProgramSkillFrame f) => _winner == Winner.None && _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId);
    private static string UniqueHpPeerCostReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer}";
    private static string UniqueHpPeerDrawReason(ProgramSkillFrame f, int seat) => $"skill-program.{f.SkillId}.unique-hp-peer.draw.{seat}";
    private IEnumerable<(Card Card, CardLocation From)> UniqueHpPeerLegalCards(ProgramSkillFrame f, int actor) =>
        new[] { CardLocation.Hand(actor), CardLocation.Equipment(actor) }.SelectMany(from => _cardZones.CardsAt(from)
            .Where(c => !IsForeignEquipmentDiscardPrevented(actor, c, from, OwnedCardMoveIntent.Discard) &&
                !(actor == f.OwnerSeat && from.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(actor, f.SkillId, f.SkillInstanceId, c)))
            .Select(c => (c, from)));
    private int UniqueHpPeerRequired(int seat) => Math.Min(2, GetHand(_players[seat]).Count + GetEquipment(_players[seat]).Count);
    private bool UniqueHpPeerPayable(ProgramSkillFrame f, int seat) => UniqueHpPeerLegalCards(f, seat).Count() >= UniqueHpPeerRequired(seat);
    private bool ExactUniqueHpPeerParent(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.OtherActualUseTargeted, ActualUseTarget: { } identity } context ||
            !IsSlashCard(identity.EffectiveKind) || context.OwnerSeat != f.OwnerSeat || identity.TargetSeat != f.OwnerSeat ||
            context.TargetSeat != identity.TargetSeat || context.SourceSeat != identity.ActorSeat ||
            _resolutionStack.OfType<ActualUseTargetWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } parent ||
            parent.ParentFrameId != identity.CardUseFrameId || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates.Count != parent.Contexts.Count || !MatchesActualUseTarget(identity)) return false;
        return MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) &&
            parent.Contexts[parent.CandidateIndex].ActualUseTarget == identity && parent.Contexts[parent.CandidateIndex] == context;
    }
    private bool CanRunUniqueHpPeer(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardDrawAndOfferUniqueHpPeer) ||
        context.ActualUseTarget is { } identity && IsSlashCard(identity.EffectiveKind) && identity.TargetSeat == c.OwnerSeat &&
        CanRunActualUseTarget(c, context) &&
        UniqueHpPeerRequired(c.OwnerSeat) <= new[] { CardLocation.Hand(c.OwnerSeat), CardLocation.Equipment(c.OwnerSeat) }
            .Sum(from => _cardZones.CardsAt(from).Count(x => !IsForeignEquipmentDiscardPrevented(c.OwnerSeat, x, from, OwnedCardMoveIntent.Discard) &&
                !(from.Zone == CardZoneKind.Equipment && IsActiveProgramSourceEquipmentCard(c.OwnerSeat, c.SkillId, c.SkillInstanceId, x))));

    private SkillProgramStepOutcome BeginUniqueHpPeer(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.UniqueHpPeer is not null || f.InstructionIndex != 1 || !ExactUniqueHpPeerParent(f) || !RecipientContestUnissuedSource(f))
            throw new InvalidOperationException("The target discard/draw requires its exact real actual-target Slash candidate.");
        var context = f.WindowContext!; var identity = context.ActualUseTarget!;
        var r = new UniqueHpPeerReceipt(1, RecipientContestSource(f), f.GameplayHash, _turnNumber, _turnProgression.OwnerSeat,
            context.ParentFrameId, identity.CardUseFrameId, identity.ActionId, identity, UniqueHpPeerStage.SelectingCost,
            f.OwnerSeat, UniqueHpPeerRequired(f.OwnerSeat), [], []);
        ReplaceRuntimeTop(f = f with { UniqueHpPeer = r });
        AdvanceEventRulesAndQueueFact(new UniqueHpPeerStartedEvent(f.Id, r.Source, r.GameplayHash, r.TurnNumber, r.TurnOwnerSeat,
            r.WindowFrameId, r.CardUseFrameId, r.CardActionId, r.TargetIdentity));
        StartUniqueHpPeerCost(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private void StartUniqueHpPeerCost(ProgramSkillFrame f)
    {
        var r = f.UniqueHpPeer!;
        if (!RecipientContestUnissuedSource(f) || !_players[r.ChooserSeat].IsAlive || !UniqueHpPeerPayable(f, r.ChooserSeat))
        { CancelProgramBindingAndCleanup(f, "原来源或完整实际HE成本失效，未付后继取消。"); return; }
        r = r with { Stage = UniqueHpPeerStage.SelectingCost, RequiredCount = UniqueHpPeerRequired(r.ChooserSeat), SelectedCardIds = [], SelectedLocations = [] };
        ReplaceRuntimeTop(f = f with { UniqueHpPeer = r });
        if (r.RequiredCount == 0) { PayUniqueHpPeerCost(f); return; }
        PublishUniqueHpPeer(f);
    }
    private IReadOnlyList<PromptChoice> UniqueHpPeerChoices(ProgramSkillFrame f)
    {
        var r = f.UniqueHpPeer!; var choices = new List<PromptChoice>();
        void Add(string branch, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets, CardLocation? from = null) => choices.Add(new(
            new($"unique-hp.{f.Id}.{r.ChooserSeat}.{r.Stage}.{r.SelectedCardIds.Count}.{branch}.{cards.FirstOrDefault()}"), label, cards, targets,
            new Dictionary<string, string> { ["program-action"] = "unique-hp-peer", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["branch"] = branch, ["source-zone"] = from?.Zone.ToString() ?? "" }));
        if (r.Stage == UniqueHpPeerStage.PeerOffer)
        {
            if (UniqueHpPeerPayable(f, r.ChooserSeat)) Add("accept", "弃两张牌（不足全弃），然后摸两张牌", [], []);
            Add("decline", "不执行", [], []);
        }
        else if (r.Stage == UniqueHpPeerStage.SelectingCost)
            foreach (var item in UniqueHpPeerLegalCards(f, r.ChooserSeat).Where(x => !r.SelectedCardIds.Contains(x.Card.Id)))
                Add("card", $"选择弃置【{item.Card.DisplayName}】", [item.Card.Id], [r.ChooserSeat], item.From);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishUniqueHpPeer(ProgramSkillFrame f)
    {
        var r = f.UniqueHpPeer!; var choices = UniqueHpPeerChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (choices.Count == 0) { CancelProgramBindingAndCleanup(f, "实际HE成本不足，未支付。"); return; }
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.ChooserSeat, r.Stage == UniqueHpPeerStage.PeerOffer ? "你是结算后唯一最高体力者，可以如此做。" : $"选择 {r.RequiredCount} 张实际HE牌一起弃置。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [r.ChooserSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.ChooserSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolveUniqueHpPeerChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertRecipientContestPrograms(f); var r = f.UniqueHpPeer!;
        if (_pendingDecision?.PlayerSeat != r.ChooserSeat || !UniqueHpPeerChoices(f).Any(c => c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets)))
            throw new InvalidOperationException("The private HE answer changed its exact original actor or entity.");
        ClearPendingDecision();
        if (!RecipientContestUnissuedSource(f) || !_players[r.ChooserSeat].IsAlive) { CancelProgramBindingAndCleanup(f, "尚未支付的来源或参与者失效。"); return; }
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (r.Stage == UniqueHpPeerStage.PeerOffer)
        {
            if (branch == "decline") { FinishProgramSkill(f, true); return; }
            if (branch != "accept") throw new InvalidOperationException("Invalid unique-HP peer option.");
            StartUniqueHpPeerCost(f); return;
        }
        if (r.Stage != UniqueHpPeerStage.SelectingCost || branch != "card" || choice.Cards is not [var id] ||
            choice.Targets is not [var seat] || seat != r.ChooserSeat || r.SelectedCardIds.Contains(id))
            throw new InvalidOperationException("Invalid exact HE selection.");
        var item = UniqueHpPeerLegalCards(f, seat).Single(x => x.Card.Id == id);
        r = r with { SelectedCardIds = r.SelectedCardIds.Append(id).ToArray(), SelectedLocations = r.SelectedLocations.Append(item.From).ToArray() };
        ReplaceRuntimeTop(f = f with { UniqueHpPeer = r });
        if (r.SelectedCardIds.Count == r.RequiredCount) PayUniqueHpPeerCost(f); else PublishUniqueHpPeer(f);
    }
    private void PayUniqueHpPeerCost(ProgramSkillFrame f)
    {
        var r = f.UniqueHpPeer!;
        if (r.SelectedCardIds.Count != r.RequiredCount || r.RequiredCount != UniqueHpPeerRequired(r.ChooserSeat) ||
            r.SelectedCardIds.Where((id, i) => !UniqueHpPeerLegalCards(f, r.ChooserSeat).Any(x => x.Card.Id == id && x.From == r.SelectedLocations[i])).Any())
            throw new InvalidOperationException("The full frozen HE cost must still be legally payable once.");
        var invoice = new UniqueHpPeerInvoice(r.ChooserSeat, r.RequiredCount, r.SelectedCardIds, r.SelectedLocations,
            RecipientContestSequence, RecipientContestSequence);
        r = r with { Stage = UniqueHpPeerStage.CostChildren, SelectedCardIds = [], SelectedLocations = [],
            OwnerInvoice = r.ChooserSeat == f.OwnerSeat ? invoice : r.OwnerInvoice, PeerInvoice = r.ChooserSeat == f.OwnerSeat ? r.PeerInvoice : invoice };
        ReplaceRuntimeTop(f = f with { UniqueHpPeer = r, PendingMovementContinuation = new(r.ChooserSeat, 0, null) });
        void Commit(long after)
        {
            var current = GetActiveProgramFrame(f.Id); var receipt = current.UniqueHpPeer!; var paid = invoice with { CostAfter = after };
            ReplaceRuntimeTop(current with { UniqueHpPeer = receipt with { OwnerInvoice = r.ChooserSeat == f.OwnerSeat ? paid : receipt.OwnerInvoice,
                PeerInvoice = r.ChooserSeat == f.OwnerSeat ? receipt.PeerInvoice : paid } });
            AdvanceEventRulesAndQueueFact(new UniqueHpPeerCostPaidEvent(f.Id, r.ChooserSeat, r.RequiredCount, paid.CostBefore, paid.CostAfter));
        }
        if (r.RequiredCount == 0) Commit(RecipientContestSequence);
        else MoveProgramCardsFromMultipleSources(invoice.CardIds, CardLocation.DiscardPile, new(UniqueHpPeerCostReason(f)), (_, records) => Commit(records.Max(m => m.Sequence)));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumeUniqueHpPeer(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.UniqueHpPeer is not { } r) return false;
        AssertRecipientContestPrograms(f);
        if (r.Stage is UniqueHpPeerStage.SelectingCost or UniqueHpPeerStage.PeerOffer)
        { if (_pendingDecision is null) PublishUniqueHpPeer(f); return true; }
        if (r.Stage is UniqueHpPeerStage.CostChildren or UniqueHpPeerStage.DrawChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (!RecipientContestUnissuedSource(f) || !_players[r.ChooserSeat].IsAlive)
            { CancelProgramBindingAndCleanup(f, "保留真实已付款及孩子，取消尚未发行的摸牌或同做机会。"); return true; }
            if (r.Stage == UniqueHpPeerStage.CostChildren)
            {
                var invoice = r.ChooserSeat == f.OwnerSeat ? r.OwnerInvoice! : r.PeerInvoice!;
                var before = RecipientContestSequence;
                ReplaceRuntimeTop(f = f with { UniqueHpPeer = r with { Stage = UniqueHpPeerStage.DrawChildren }, PendingMovementContinuation = new(r.ChooserSeat, 0, null) });
                var count = DrawCards(_players[r.ChooserSeat], 2, true, new(UniqueHpPeerDrawReason(f, r.ChooserSeat))).Count;
                invoice = invoice with { DrawIssued = true, DrawBefore = before, DrawAfter = RecipientContestSequence, ActualDrawCount = count };
                var current = GetActiveProgramFrame(id);
                ReplaceRuntimeTop(current with { UniqueHpPeer = current.UniqueHpPeer! with { OwnerInvoice = r.ChooserSeat == f.OwnerSeat ? invoice : r.OwnerInvoice,
                    PeerInvoice = r.ChooserSeat == f.OwnerSeat ? r.PeerInvoice : invoice } });
                AdvanceEventRulesAndQueueFact(new UniqueHpPeerDrawIssuedEvent(id, r.ChooserSeat, before, invoice.DrawAfter, count));
                AdvanceRuntimeProgram(id); return true;
            }
            if (r.ChooserSeat != f.OwnerSeat) { FinishProgramSkill(f, true); return true; }
            var living = _players.Where(p => p.IsAlive).ToArray(); var maximum = living.Max(p => p.Hp);
            var peers = living.Where(p => p.Hp == maximum).ToArray(); var peer = peers.Length == 1 && peers[0].Seat != f.OwnerSeat ? peers[0] : null;
            AdvanceEventRulesAndQueueFact(new UniqueHpPeerOfferedEvent(id, peer?.Seat, peer?.Hp));
            if (peer is null) { FinishProgramSkill(f, true); return true; }
            ReplaceRuntimeTop(f = f with { UniqueHpPeer = r with { Stage = UniqueHpPeerStage.PeerOffer, ChooserSeat = peer.Seat, RequiredCount = 0, PeerSeat = peer.Seat, PeerHp = peer.Hp } });
            PublishUniqueHpPeer(f); return true;
        }
        FinishProgramSkill(f, true); return true;
    }
    private PromptChoice SelectAiRecipientContest(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.UniqueHpPeer is { } r)
        {
            if (r.Stage == UniqueHpPeerStage.PeerOffer)
            {
                var cost = UniqueHpPeerLegalCards(f, r.ChooserSeat).Select(x => GetKeepValue(x.Card, _players[r.ChooserSeat])).Order().Take(UniqueHpPeerRequired(r.ChooserSeat)).Sum();
                return decision.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == (cost < 24d && decision.Choices.Any(x => x.Parameters.GetValueOrDefault("branch") == "accept") ? "accept" : "decline"));
            }
            return decision.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[r.ChooserSeat])).ThenBy(c => c.Cards.Single()).First();
        }
        // A one-point damage to the selected target is what this effect promises;
        // score it through the shared target-loss hint shape.
        return decision.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets.Single(), new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false))).ThenBy(c => c.Targets.Single()).First();
    }
}
