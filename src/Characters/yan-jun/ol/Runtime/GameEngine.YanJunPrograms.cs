namespace CardGame.Core;

// 观潮 evidence: the committed phase pattern is public, and the phase-start
// event index pins the choice to exactly one play-phase instance.
public sealed record ProgramGuanchaoPatternChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int PhaseEventIndex, bool Ascending) : IGameEvent;
public sealed record ProgramGuanchaoRankDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int Rank, bool Drew) : IGameEvent;

// 逊贤 evidence: one scalar per used/played card that finished into the discard
// pile, attributed while the acting use frame is still live; the gift itself is
// public once it moves.
public sealed record SettledActionCardDiscardEvent(long BatchId, int MovementSequence, int CardId,
    int ActorSeat, int TurnNumber) : IGameEvent;
public sealed record ProgramXunxianGiftEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, int CardId, int DiscardMovementSequence) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksSettledActionCards =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.XunxianGiftUsedCard) == true ||
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.TongxieFollowUp) == true;

    // One scalar fact per settled use/response card. The attribution has to be
    // captured at batch completion, while the acting use frame is still on the
    // resolution stack; the cards-moved window opens later and reads the facts.
    private void CaptureSettledActionCards(long batchId, int turnNumber, IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksSettledActionCards) return;
        foreach (var movement in movements)
        {
            if (movement.To != CardLocation.DiscardPile || movement.From != CardLocation.Processing ||
                movement.Reason.Value is not ("card.use-finished" or "card.response-finished" or "card.respond.nullification-finished"))
                continue;
            var action = SettledActionContext(movement.CardId);
            if (action is null) continue;
            AdvanceEventRulesAndQueueFact(new SettledActionCardDiscardEvent(batchId, movement.Sequence,
                movement.CardId, action.ActorSeat, turnNumber));
            CaptureTongxieSettledSlashUse(batchId, movement);
        }
    }

    private CardActionContext? SettledActionContext(int cardId)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Select(frame => frame.Action).LastOrDefault(action =>
            action is not null && action.Type == CardActionType.Use && action.PhysicalCards.Any(cost => cost.CardId == cardId));
        if (use is not null) return use;
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Action is not null)?.Action;
        if (parent is null) return null;
        return CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(accepted => accepted.Action)
            .LastOrDefault(action => action.Type == CardActionType.Response && action.ParentActionId == parent.ActionId &&
                action.PhysicalCards.Any(cost => cost.CardId == cardId));
    }

    private int[] MatchingOwnSettledActionCardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (!TracksSettledActionCards) return [];
        var settled = CompleteProgramEventHistory().OfType<SettledActionCardDiscardEvent>()
            .Where(e => e.BatchId == batch.Id && e.ActorSeat == candidate.OwnerSeat)
            .Select(e => e.MovementSequence).ToHashSet();
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => settled.Contains(item.movement.Sequence) && item.movement.To == CardLocation.DiscardPile &&
                _cardZones.GetLocation(item.movement.CardId) == CardLocation.DiscardPile)
            .Select(item => item.index).Order().ToArray();
    }

    private int CurrentPlayPhaseEventIndex(int ownerSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        return Array.FindLastIndex(facts, e => e is PhaseChangedEvent phase &&
            phase.Phase == TurnPhase.Play && phase.ActorSeat == ownerSeat);
    }



    [ProgramCapability]
    private sealed class YanJunProgramCapability(GameEngine engine) : IYanJunProgramHost
    {
        public SkillProgramStepOutcome GuanchaoChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.GuanchaoProgramChoosePattern(frame, effect);
        public SkillProgramStepOutcome GuanchaoRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.GuanchaoProgramRankDraw(frame, effect);
        public SkillProgramStepOutcome XunxianGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.XunxianProgramGiftUsedCard(frame, effect);
    }
}
