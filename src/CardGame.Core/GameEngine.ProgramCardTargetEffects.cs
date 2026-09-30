namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void NullifySelectedProgramCardEffects(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not
            { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
              CardUse: { } cardUse } context || active.SelectedTargetSeats.Count == 0 ||
            active.SelectedTargetSeats.Distinct().Count() != active.SelectedTargetSeats.Count)
            throw new InvalidOperationException("Selected card-effect nullification requires a frozen nonempty target set.");
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is not { } window ||
            window.Id != context.ParentFrameId || window.Action.ActionId != cardUse.CardActionId ||
            window.ParentFrameId != cardUse.ParentCardUseFrameId ||
            window.Action.Type != CardActionType.Use ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(item => item.Id == window.ParentFrameId) is not { } parent ||
            parent.Action?.ActionId != cardUse.CardActionId ||
            parent.Action.EffectiveKind != cardUse.EffectiveKind ||
            parent.Action.ActorSeat != cardUse.ActorSeat)
            throw new InvalidOperationException("Selected card-effect nullification lost its frozen parent card action.");

        var designated = (cardUse.DesignatedTargetSeats ?? parent.Action.EffectiveDesignatedTargetSeats).ToHashSet();
        if (active.SelectedTargetSeats.Any(seat => !designated.Contains(seat)))
            throw new InvalidOperationException("A selected seat was never a target of the current card use.");
        var newlyNullified = active.SelectedTargetSeats.Where(seat => _players[seat].IsAlive &&
            parent.IneffectiveTargetSeats?.Contains(seat) != true).ToArray();
        foreach (var seat in newlyNullified)
            MarkCardEffectIneffective(parent.Id, seat);
        if (newlyNullified.Length == 0) return;
        QueueGameEvent(new ProgramSelectedCardEffectsNullifiedEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, cardUse.ActorSeat,
            parent.Id, cardUse.EffectiveKind, newlyNullified));
        foreach (var seat in newlyNullified)
            QueueGameEvent(new CardEffectSkippedEvent(parent.Id, cardUse.ActorSeat, seat,
                cardUse.EffectiveKind, CardEffectSkipReason.SkillNullified));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，" +
            $"令【{CardCatalog.Get(cardUse.EffectiveKind).DisplayName}】对" +
            $"{string.Join("、", newlyNullified.Select(seat => _players[seat].Name))}无效。",
            active.OwnerSeat, cardUse.ActorSeat);
    }

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

    private bool IsCardEffectIneffective(long resolutionId, int targetSeat)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == resolutionId);
        if (frame.IneffectiveTargetSeats?.Contains(targetSeat) == true) return true;
        if (frame.Action is not { } action || !GrantTurnCardEffectImmunityProgramOperationDescriptor.CardEffectImmunityKinds.Contains(action.EffectiveKind) ||
            _turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, targetSeat, SkillRuleQuery.CardEffectImmunity, action.EffectiveKind).Count == 0) return false;
        MarkCardEffectIneffective(resolutionId, targetSeat);
        QueueGameEvent(new CardEffectSkippedEvent(resolutionId, action.ActorSeat, targetSeat, action.EffectiveKind, CardEffectSkipReason.SkillNullified));
        return true;
    }

    private void CompleteIneffectiveTrickTarget(
        NullificationResolution pending,
        int targetSeat)
    {
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        foreach (var physicalCard in GetCardUsePhysicalCards(pending.ResolutionId))
        {
            MoveFinishedTrickCard(pending.ResolutionId, physicalCard);
        }
        AddLog(
            "CardEffect",
            $"【{CardCatalog.Get(pending.EffectiveCardKind).DisplayName}】对 {_players[targetSeat].Name} 无效。",
            targetSeat,
            pending.SourceSeat);
        FinishCardUse(pending.ResolutionId, pending.EffectCard, pending.EffectiveCardKind);
    }
}
