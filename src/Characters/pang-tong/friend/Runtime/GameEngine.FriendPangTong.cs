using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int FriendActualPlayHandLossCount(int seat) => EventsSinceLastBoundary(payload =>
        payload is PhaseChangedEvent phase && phase.Phase == TurnPhase.Play && phase.ActorSeat == seat)
        .OfType<CardMovedEvent>().Count(move => move.From == CardLocation.Hand(seat) && move.To != move.From);
    private IReadOnlyList<int> FriendGainCardIds(ProgramSkillWindowContext context, int seat) => context.MovementBatch is { } batch
        ? batch.NativeDrawInvocation is { } draw && draw.OwnerSeat == seat && ValidNativeDrawInvocation(draw)
            ? draw.Materials.Select(material => material.CardId).ToArray() : batch.Movements.Where(move => move.To == CardLocation.Hand(seat) && move.From != move.To)
            .Select(move => move.CardId).Distinct().ToArray() : [];
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 670)]
    private bool CanOfferFriendPangTong(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect]) return true;
        if (effect.Op == SkillProgramEffectOp.ExchangeFriendGainForDiscard)
            return context.MovementBatch is { } batch && batch.OriginSkillId != candidate.SkillId &&
                FriendGainCardIds(context, candidate.OwnerSeat).Count >= 2 &&
                FriendGainCardIds(context, candidate.OwnerSeat).Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(candidate.OwnerSeat));
        if (effect.Op == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd)
            return context.Window == SkillProgramTriggerWindow.PlayEnding && context.TargetSeat == candidate.OwnerSeat &&
                FriendActualPlayHandLossCount(candidate.OwnerSeat) >= 3;
        return true;
    }
    private IReadOnlyList<PromptChoice> FriendPangTongChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.FriendPangTong!; var choices = new List<PromptChoice>();
        Dictionary<string,string> Parameters(string value) => new()
        { ["program-action"] = "friend-pang-tong", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["value"] = value };
        if (receipt.Stage == FriendPangTongStage.Select)
        {
            foreach (var id in receipt.Cards.Except(receipt.Selected).Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)))
            {
                var card = GetAttackCard(id);
                choices.Add(new(new($"friend-manjuan.{frame.Id}.{receipt.Selected.Count}.{id}"),
                    $"选择【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)}{card.RankText}），按选择顺序从顶至底放置", [id], [], Parameters(id.ToString(CultureInfo.InvariantCulture))));
            }
            choices.Add(new(new($"friend-manjuan.{frame.Id}.finish"), receipt.Selected.Count == 0 ? "不放置牌" : $"放置已选 {receipt.Selected.Count} 张牌并换牌", [], [], Parameters("finish")));
        }
        else if (receipt.Stage == FriendPangTongStage.Use)
        {
            foreach (var (action, index) in FriendYangmingActions(frame).Select((action, index) => (action, index)))
                choices.Add(new(new($"friend-yangming.{frame.Id}.{receipt.UsedSuits.Count}.{index}"), action.Description,
                    [action.CardId!.Value], action.TargetSeats, Parameters(index.ToString(CultureInfo.InvariantCulture))));
            choices.Add(new(new($"friend-yangming.{frame.Id}.finish"), "结束使用，将剩余牌置入弃牌堆", [], [], Parameters("finish")));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishFriendPangTongChoice(ProgramSkillFrame frame)
    {
        var choices = FriendPangTongChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.FriendPangTong!.Stage == FriendPangTongStage.Select ? "漫卷：选择本次获得的牌，按从顶至底的顺序置于牌堆顶。" : "养名：使用武将牌上的牌，每种花色只能使用一次。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("friend-pang-tong")]
    private void ResolveFriendPangTongChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Friend Pang Tong lost its program.");
        AssertFriendPangTong(frame);
        var canonical = FriendPangTongChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id);
        if (canonical is null || !AssistedChoicesEqual([canonical], [selected])) throw new InvalidOperationException("Friend Pang Tong changed its exact private cards, order or use targets.");
        ClearPendingDecision();
        var receipt = frame.FriendPangTong!; var value = selected.Parameters["value"];
        if (receipt.Stage == FriendPangTongStage.Select)
        {
            if (value != "finish")
            { ReplaceRuntimeTop(frame with { FriendPangTong = receipt with { Selected = receipt.Selected.Append(selected.Cards.Single()).ToArray() } }); PublishFriendPangTongChoice(GetActiveProgramFrame(frame.Id)); return; }
            if (receipt.Selected.Count == 0) { FinishFriendPangTong(frame); return; }
            var cards = receipt.Selected.Select(GetAttackCard).ToArray();
            FriendPangTongMutation(frame, FriendPangTongStage.Exchange, () =>
            {
                MoveCards(cards, CardLocation.Hand(frame.OwnerSeat), CardLocation.DrawPile, new("friend.manjuan.top"));
                _cardZones.PlaceDrawPileCardsAtTop(receipt.Selected);
            }); return;
        }
        if (value == "finish") { CleanupFriendYangming(frame); return; }
        var action = FriendYangmingActions(frame)[int.Parse(value, CultureInfo.InvariantCulture)];
        IssueFriendYangmingUse(frame, action);
    }
    private void ContinueFriendPangTong(ProgramSkillFrame frame)
    {
        ReplaceRuntimeTop(frame); var receipt = frame.FriendPangTong!;
        if (receipt.Stage == FriendPangTongStage.Complete) { FinishFriendPangTong(frame); return; }
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { if (receipt.Operation == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd) CleanupFriendYangming(frame, reward: false); else FinishFriendPangTong(frame); return; }
        switch (receipt.Stage)
        {
            case FriendPangTongStage.Exchange:
                if (receipt.ExchangeIndex >= Math.Min(5, receipt.Selected.Count)) { FinishFriendPangTong(frame); return; }
                var original = GetAttackCard(receipt.Selected[receipt.ExchangeIndex]); var category = FriendCategoryValue(original.Kind);
                var pool = _cardZones.CardsAt(CardLocation.DiscardPile).Where(card => FriendCategoryValue(card.Kind) != category).OrderBy(card => card.Id).ToArray();
                ReplaceRuntimeTop(frame = frame with { FriendPangTong = receipt with { ExchangeIndex = receipt.ExchangeIndex + 1 } });
                FriendPangTongMutation(frame, FriendPangTongStage.Exchange, () =>
                { if (pool.Length > 0) MoveCard(pool[_random.Next(pool.Length)], CardLocation.DiscardPile, CardLocation.Hand(frame.OwnerSeat), new("friend.manjuan.gain")); }); return;
            case FriendPangTongStage.Returned:
                FriendPangTongMutation(frame, FriendPangTongStage.Use, () => { }); return;
            case FriendPangTongStage.Use:
                if (FriendYangmingActions(frame).Count == 0) { CleanupFriendYangming(frame); return; }
                PublishFriendPangTongChoice(frame); return;
            case FriendPangTongStage.Reward:
                var excluded = receipt.UsedSuits;
                var reward = HasRuntimeSkill(_players[frame.OwnerSeat], "friend:gongli-pang-tong") && HasFriendTrioAlly(_players[frame.OwnerSeat], "xu-shu")
                    ? _cardZones.CardsAt(CardLocation.DrawPile).Where(card => receipt.Cards.Select(GetAttackCard).Any(placed => placed.Suit == card.Suit) &&
                        !excluded.Contains(card.Suit)).OrderBy(card => card.Id).ToArray() : [];
                FriendPangTongMutation(frame, FriendPangTongStage.Complete, () =>
                { if (reward.Length > 0) MoveCard(reward[_random.Next(reward.Length)], CardLocation.DrawPile, CardLocation.Hand(frame.OwnerSeat), new("friend.gongli-pang.gain")); }); return;
            case FriendPangTongStage.Complete: FinishFriendPangTong(frame); return;
            default: if (_pendingDecision is null) PublishFriendPangTongChoice(frame); return;
        }
    }
    private void FinishFriendPangTong(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(frame with { FriendPangTong = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(frame.Id); }
    private void FriendPangTongMutation(ProgramSkillFrame frame, FriendPangTongStage next, Action mutation)
    {
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = frame.FriendPangTong! with
            { Stage = FriendPangTongStage.Movement, NextStage = next, Before = before, After = before }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        mutation(); frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { FriendPangTong = frame.FriendPangTong! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new FriendPangTongMovementPaidEvent(frame.Id, frame.FriendPangTong.Operation, before, _movementSequence));
        ReturnFriendPangTongMovement(frame);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20)]
    private bool ReturnFriendPangTongMovement(ProgramSkillFrame frame)
    {
        if (frame.FriendPangTong is not { Stage: FriendPangTongStage.Movement } receipt) return false;
        if (DrainFriendTrioChildren(frame)) return true;
        ContinueFriendPangTong(frame with { PendingMovementContinuation = null, FriendPangTong = receipt with { Stage = receipt.NextStage } }); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20)]
    private bool ResumeFriendPangTong(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FriendPangTong: { } receipt } frame || frame.Id != id) return false;
        if (receipt.Stage == FriendPangTongStage.Movement) return ReturnFriendPangTongMovement(frame);
        if (receipt.Stage == FriendPangTongStage.Issued) return true;
        if (_pendingDecision is null) ContinueFriendPangTong(frame); return true;
    }
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 90)]
    private bool IsFriendPangTongMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        frame.FriendPangTong is { Stage: FriendPangTongStage.Movement } receipt && effect?.Op == receipt.Operation &&
        frame.PendingMovementContinuation == pending && pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 470)]
    private void AssertFriendPangTong(ProgramSkillFrame frame)
    {
        if (frame.FriendPangTong is not { } receipt) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (receipt.InstructionIndex != frame.InstructionIndex || effect.Op != receipt.Operation || !Enum.IsDefined(receipt.Stage) ||
            receipt.Cards.Distinct().Count() != receipt.Cards.Count || receipt.Selected.Distinct().Count() != receipt.Selected.Count ||
            receipt.Selected.Any(id => !receipt.Cards.Contains(id)) || receipt.UsedSuits.Distinct().Count() != receipt.UsedSuits.Count ||
            receipt.Stage == FriendPangTongStage.Issued != (receipt.UseId is not null) ||
            receipt.Stage == FriendPangTongStage.Movement && (frame.PendingMovementContinuation is null || receipt.Before > receipt.After || receipt.After > _movementSequence))
            throw new InvalidOperationException("Friend Pang Tong lost its frozen cards, ordered exchange or one-use receipt.");
    }
    [ProgramViewContributor(ProgramViewContribution.PublicCardsAfterResolution, 20)]
    private IEnumerable<CardSnapshot> FriendPangTongPublicCards() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(frame => frame.FriendPangTong?.Operation == SkillProgramEffectOp.UseFriendDeckSuitsAtPlayEnd)
        .SelectMany(frame => _cardZones.CardsAt(FriendYangmingLocation(frame))).Select(ToSnapshot);
}
