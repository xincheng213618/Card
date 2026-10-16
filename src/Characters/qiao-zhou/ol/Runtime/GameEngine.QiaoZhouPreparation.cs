namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasQiaoZhouPreparationBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not QiaoZhouPreparationFrame f) return false;
        AssertQiaoZhouPreparation(f); return true;
    }
    private void AssertQiaoZhouPreparation(QiaoZhouPreparationFrame f)
    {
        if (f.OwnerSeat != _currentSeat || f.ActualTurnNumber != _turnNumber || f.Grant.RecipientSeat != f.OwnerSeat ||
            f.Grant.RedCount is < 0 or > 1 || !Enum.IsDefined(f.Stage) || f.SequenceBefore < 0 || f.SequenceAfter < f.SequenceBefore ||
            f.SequenceAfter > _movementSequence || ProgramEventHistory<QiaoZhouForecastGrantedEvent>().Count(e => e == f.Grant) != 1 ||
            QiaoZhouActivation(f.Grant.GrantId) is not { } activation || activation.ActualTurnNumber != f.ActualTurnNumber ||
            activation.RecipientSeat != f.OwnerSeat || f.Stage == QiaoZhouPreparationStage.Selecting && f.PaidCardId is not null ||
            f.Stage == QiaoZhouPreparationStage.MovementChildren && (f.PaidCardId is not { } paid ||
                f.SequenceAfter != f.SequenceBefore + 1 || _cardMovements.Count(m => m.Sequence == f.SequenceAfter && m.CardId == paid &&
                    m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason.Value == QiaoZhouPreparationReason) != 1 ||
                ProgramEventHistory<QiaoZhouPreparationDiscardedEvent>().Count(e => e.FrameId == f.Id && e.GrantId == f.Grant.GrantId &&
                    e.RecipientSeat == f.OwnerSeat && e.CardId == paid && e.SequenceBefore == f.SequenceBefore && e.SequenceAfter == f.SequenceAfter) != 1))
            throw new InvalidOperationException("Xingbu preparation lost its exact issued mark, actual turn or one paid hand card.");
        if (f.ActiveChildFrameId is { } childId && (_resolutionStack.Count < 2 ||
            _resolutionStack[1] is not CardsMovedTriggerWindowFrame child || child.Id != childId || !IsQiaoZhouPreparationMovement(f, child)))
            throw new InvalidOperationException("Xingbu preparation lost its exact native discard child.");
    }
    private Card[] QiaoZhouPreparationCards(QiaoZhouPreparationFrame f) => GetHand(_players[f.OwnerSeat]).Where(card =>
        !card.IsGeneralWeapon && !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, CardLocation.Hand(f.OwnerSeat), OwnedCardMoveIntent.Discard)).ToArray();
    private IReadOnlyList<PromptChoice> QiaoZhouPreparationChoices(QiaoZhouPreparationFrame f) => Array.AsReadOnly(
        QiaoZhouPreparationCards(f).Select(card => new PromptChoice(new($"qiao-zhou-preparation.{f.Id}.{card.Id}"),
            $"星卜：弃置【{card.DisplayName}】{card.RankText}", [card.Id], [],
            new Dictionary<string, string> { ["program-action"] = "qiao-zhou-preparation" })).ToArray());
    private void ContinueQiaoZhouPreparation(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not QiaoZhouPreparationFrame f || f.Id != frameId) return;
        AssertQiaoZhouPreparation(f);
        if (f.ActiveChildFrameId is not null) throw new InvalidOperationException("Xingbu preparation resumed before its child returned.");
        if (f.Stage == QiaoZhouPreparationStage.MovementChildren && TryBeginQiaoZhouPreparationMovement(f)) return;
        if (f.Stage == QiaoZhouPreparationStage.MovementChildren || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            !QiaoZhouMarks().Any(g => g == f.Grant) || QiaoZhouPreparationCards(f).Length == 0)
        {
            PopResolutionFrame(f.Id, ResolutionFrameKind.QiaoZhouPreparation);
            if (_winner != Winner.None) { AdvanceRulesAndPublishState(); return; }
            if (!_players[f.OwnerSeat].IsAlive) { EndTurn(); return; }
            if (!TryBeginTurnStartProgramWindow(_players[f.OwnerSeat])) BeginNormalTurnStartAfterProgramBindings(_players[f.OwnerSeat]);
            return;
        }
        if (!_players[f.OwnerSeat].IsHuman)
        {
            var id = QiaoZhouPreparationCards(f).OrderBy(c => CardCatalog.Get(c.Kind).HandKeepValue).ThenBy(c => c.Id).First().Id;
            PayQiaoZhouPreparation(f, id); return;
        }
        var choices = QiaoZhouPreparationChoices(f); var skill = _contentRegistry.GetSkill("ol:xingbu");
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "星卜：准备阶段须弃置一张手牌。", choices.SelectMany(c => c.Cards).ToArray(), [], f.Grant.Source.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = EngineStatus.AwaitingHumanResponse;
    }
    [ProgramAnswer("qiao-zhou-preparation")]
    private void AnswerQiaoZhouPreparation(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not QiaoZhouPreparationFrame { Stage: QiaoZhouPreparationStage.Selecting } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], QiaoZhouPreparationChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Xingbu preparation lost its recipient's exact private hand choice.");
        AssertQiaoZhouPreparation(f); ClearPendingDecision(); PayQiaoZhouPreparation(f, choice.Cards.Single());
    }
    private void PayQiaoZhouPreparation(QiaoZhouPreparationFrame f, int cardId)
    {
        if (f.PaidCardId is not null || !QiaoZhouPreparationCards(f).Any(c => c.Id == cardId))
            throw new InvalidOperationException("Xingbu preparation cannot pay twice or discard another zone.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { Stage = QiaoZhouPreparationStage.MovementChildren, Step = ResolutionFrameStep.ResolvingEffect,
            PaidCardId = cardId, SequenceBefore = before, SequenceAfter = before });
        MoveProgramCardsFromMultipleSources([cardId], CardLocation.DiscardPile, new(QiaoZhouPreparationReason), (_, _) =>
        {
            var actual = (QiaoZhouPreparationFrame)_resolutionStack.Last();
            ReplaceRuntimeTop(actual with { SequenceAfter = _movementSequence });
            AdvanceEventRulesAndQueueFact(new QiaoZhouPreparationDiscardedEvent(f.Id, f.Grant.GrantId, f.OwnerSeat, cardId, before, _movementSequence));
        });
        ContinueQiaoZhouPreparation(f.Id);
    }
    private bool TryBeginQiaoZhouPreparationMovement(QiaoZhouPreparationFrame f)
    {
        while (_pendingCardsMovedBatches.Where(b => b.ParentFrameId == f.Id).OrderBy(b => b.Id).FirstOrDefault() is { } batch)
        {
            _pendingCardsMovedBatches.Remove(batch);
            var child = CreateCardsMovedProgramWindow(batch);
            if (child is null) continue;
            child = child with { QiaoZhouPreparationReturn = new(f.Id, f.Grant.GrantId, f.OwnerSeat, f.ActualTurnNumber, batch.Id) };
            ReplaceRuntimeTop(f = f with { ActiveChildFrameId = child.Id });
            PushRuntimeFrame(child); AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>(); return true;
        }
        return false;
    }
    private bool IsQiaoZhouPreparationMovement(QiaoZhouPreparationFrame f, CardsMovedTriggerWindowFrame child) =>
        child.QiaoZhouPreparationReturn is { } r && r.ParentFrameId == f.Id && r.GrantId == f.Grant.GrantId &&
        r.OwnerSeat == f.OwnerSeat && r.ActualTurnNumber == f.ActualTurnNumber && r.BatchId == child.Batch.Id &&
        child.Batch.ParentFrameId == f.Id && child.Batch.AwaitingProgramFrameId is null &&
        child.Batch.Movements is [{ } move] && move.CardId == f.PaidCardId && move.Sequence == f.SequenceAfter &&
        move.Sequence == f.SequenceBefore + 1 && move.From == CardLocation.Hand(f.OwnerSeat) && move.To == CardLocation.DiscardPile &&
        move.Reason.Value == QiaoZhouPreparationReason && _cardMovements.Contains(move);
    private bool ReturnQiaoZhouPreparationMovement(CardsMovedTriggerWindowFrame child)
    {
        if (child.QiaoZhouPreparationReturn is null) return false;
        if (_resolutionStack.LastOrDefault() is not QiaoZhouPreparationFrame f || f.ActiveChildFrameId != child.Id || !IsQiaoZhouPreparationMovement(f, child))
            throw new InvalidOperationException("Xingbu preparation lost its paid movement return.");
        ReplaceRuntimeTop(f with { ActiveChildFrameId = null }); ContinueQiaoZhouPreparation(f.Id); return true;
    }
}
