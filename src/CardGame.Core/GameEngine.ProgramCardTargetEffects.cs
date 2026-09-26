namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void NullifyCurrentProgramCardEffect(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var context = active.WindowContext;
        if (context is not
            {
                Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                CardUse: { } cardUse,
                TargetSeat: var targetSeat
            } || targetSeat != active.OwnerSeat)
            throw new InvalidOperationException(
                "Current card-effect nullification requires the target owner's before-target-effects context.");

        var cardUseIndex = _resolutionStack.FindLastIndex(item =>
            item.Id == cardUse.ParentCardUseFrameId && item is CardUseFrame);
        if (cardUseIndex < 0 || _resolutionStack[cardUseIndex] is not CardUseFrame parent ||
            parent.Action?.ActionId != cardUse.CardActionId ||
            !parent.TargetSeats.Contains(active.OwnerSeat) ||
            parent.Action.ActorSeat != cardUse.ActorSeat ||
            parent.Action.EffectiveKind != cardUse.EffectiveKind)
            throw new InvalidOperationException(
                "Current card-effect nullification lost its frozen parent card action.");

        MarkCardEffectIneffective(parent.Id, active.OwnerSeat);
        QueueGameEvent(new ProgramCardEffectNullifiedEvent(
            active.Id,
            active.SkillId,
            GetProgramBindingId(active),
            active.OwnerSeat,
            cardUse.ActorSeat,
            parent.Id,
            cardUse.EffectiveKind));
        QueueGameEvent(new CardEffectSkippedEvent(
            parent.Id,
            cardUse.ActorSeat,
            active.OwnerSeat,
            cardUse.EffectiveKind,
            CardEffectSkipReason.SkillNullified));
        AddLog(
            "SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            $"令【{CardCatalog.Get(cardUse.EffectiveKind).DisplayName}】对自己无效。",
            active.OwnerSeat,
            cardUse.ActorSeat);
    }

    private void MarkCardEffectIneffective(long resolutionId, int targetSeat)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == resolutionId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame cardUse)
            throw new InvalidOperationException("Card-effect nullification requires an active CardUse frame.");

        var seats = (cardUse.IneffectiveTargetSeats ?? [])
            .Append(targetSeat)
            .Distinct()
            .Order()
            .ToArray();
        _resolutionStack[index] = cardUse with { IneffectiveTargetSeats = seats };
    }

    private bool IsCardEffectIneffective(long resolutionId, int targetSeat) =>
        _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == resolutionId)
            .IneffectiveTargetSeats?.Contains(targetSeat) == true;

    private void CompleteIneffectiveTrickTarget(
        NullificationResolution pending,
        int targetSeat)
    {
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        foreach (var physicalCard in GetCardUsePhysicalCards(pending.ResolutionId))
        {
            MoveCard(
                physicalCard,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        AddLog(
            "CardEffect",
            $"【{CardCatalog.Get(pending.EffectiveCardKind).DisplayName}】对 {_players[targetSeat].Name} 无效。",
            targetSeat,
            pending.SourceSeat);
        FinishCardUse(pending.ResolutionId, pending.EffectCard, pending.EffectiveCardKind);
    }
}
