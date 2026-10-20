namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Each producer proves its own issuance before sharing native zero-entity
    // processing boundaries. A different producer cannot lend it a receipt.
    private bool IsZeroEntityUse(long id) => IsProgramVirtualOrdinaryTrickUse(id) || IsTianChouShandaoUse(id) || IsTieredRoundZeroUse(id) || IsMouZhijueZeroUse(id) || IsChosenAssaultUse(id) || IsQinLangXianyingUse(id) || IsAHuiNanJuemanUse(id) || IsDiouZeroUse(id) || IsMouLiuChengZeroUse(id) || IsMouXuHuangZeroUse(id) || IsMouWenChouZeroDuel(id);
    private bool IsZeroEntityProcessingParent(long id, int cardId) => cardId == 0 && IsZeroEntityUse(id);
    private bool MatchesMouZhijueUseAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && IsMouZhijueZeroUse(id) && LifecycleCardUse(id)?.Action is { } issued &&
        action.ActionId == issued.ActionId && action.Type == CardActionType.Use &&
        action.EffectiveKind == issued.EffectiveKind && action.PhysicalCards.Count == 0 &&
        action.ConversionChain.Count == 0 && action.ActorSeat == issued.ActorSeat && action.ProviderSeat == issued.ProviderSeat;

    private bool IsMouZhijueZeroAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (!IsMouZhijueZeroUse(attack.ResolutionId) || attack.Card is not null || attack.PhysicalCards.Count != 0 ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || attack.EffectiveCardKind != use.CardKind) return false;
        if (IsSlashCard(use.CardKind) || use.CardKind is CardKind.Duel or CardKind.FireAttack)
            return processing.Count == 0 && attack.SourceSeat == use.SourceSeat &&
                (use.CardKind != CardKind.FireAttack || CompleteProgramEventHistory().OfType<FireAttackResolvedEvent>().Any(fact =>
                    fact.ResolutionId == use.Id && fact.CausedDamage && fact.MatchingDiscardCardId > 0));
        return processing.Count == 0 && use.CardKind is CardKind.BarbarianAssault or CardKind.ArrowBarrage &&
            ActiveGroupCard is { } group && group.ResolutionId == use.Id && group.DamageSourceSeat == attack.SourceSeat &&
            group.CurrentAttack?.ResolutionId == use.Id;
    }
    private bool IsMouZhijueBorrowedAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        ActiveBorrowedSword is { ActiveAttack: { } child } borrowed && SameAttackOwner(child, attack) &&
        IsMouZhijueZeroUse(borrowed.ResolutionId) && attack.Card is not null &&
        processing.Select(c => c.Id).Order().SequenceEqual(attack.PhysicalCards.Where(c =>
            _cardZones.GetLocation(c.Id) == CardLocation.Processing).Select(c => c.Id).Order());

    [ProgramDamageHook(ProgramDamagePhase.IssuedAttackCompletion, 120)]
    private bool FinishMouZhijueZeroAttack(CardAttackHandle attack, out bool paused)
    {
        paused = false;
        if (!IsMouZhijueZeroUse(attack.ResolutionId) || attack.Card is not null ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) && kind is not (CardKind.Duel or CardKind.FireAttack)) return false;
        var use = LifecycleCardUse(attack.ResolutionId)!;
        SetCardUseStep(use.Id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(use.Id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(attack, use.Action!, SkillProgramTriggerWindow.CardUseCompleted,
            use.TargetSeats, ProgramCardContinuation.CompletedSlash, cardUseCausedDamage: attack.CardUseCausedDamage)) paused = true;
        else { PopFinishedCardUse(use.Id); CompleteFinishedAttackCardUse(attack); }
        return true;
    }
}
