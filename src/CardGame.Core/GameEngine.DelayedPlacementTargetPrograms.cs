namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static LegalActionKind DelayedPlacementActionKind(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_DelayedPlacementTargetPrograms.DelayedPlacementActionKind(kind);

    private bool TryBeginFinalizedDelayedPlacementPrograms(long frameId, Card card,
        LegalActionKind actionKind)
    {
        // This opt-in bridge is shared by physical play and the existing real
        // discarded-card and owned-card conversion producers. Their paid
        // movement children and committed-use observers have drained before
        // entering this method, including when protection will end the use.
        if (!(HasMouSimaShi || HasMouHuangQuan || HasMouXuHuangCapability || HasDiouCapability || HasLiuYanCapability || HasBianFuRenCapability || HasCaoChunCapability || HasNeifaCapability || HasSoleTargetProtectionCapability || HasSuFeiCapability || HasJianjiNativeUseCapability || HasFanChouCapability || HasGuoSiCapability) ||
            LifecycleCardUse(frameId) is not { } use || !IsDelayedCard(use.CardKind) ||
            use.FinalizedTrickProgramsStarted) return false;
        if (_resolutionStack.LastOrDefault()?.Id != frameId ||
            use.Action is not { Type: CardActionType.Use } action ||
            action.ActionId <= 0 || action.ActorSeat != use.SourceSeat ||
            action.EffectiveKind != use.CardKind || use.CardId != card.Id ||
            actionKind != DelayedPlacementActionKind(use.CardKind) ||
            action.TargetSeats is not [var target] || !IsValidPlayerSeat(target) ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !(action.PhysicalCards.Any(cost => cost.CardId == card.Id) || IsDiouUse(frameId)) ||
            _cardZones.GetLocation(card.Id) != CardLocation.Processing)
            throw new InvalidOperationException("A delayed placement lost its exact paid use and target.");
        TryMarkProgramUseAccepted(frameId);
        if (!TryMarkFinalizedTrickProgramsStarted(frameId)) return false;
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        return TryBeginProgramCardWindow(null, action,
            SkillProgramTriggerWindow.CardUseTargetsFinalized, use.TargetSeats,
            ProgramCardContinuation.FinalizedDelayedPlacement,
            trickContinuation: new(card.Id, actionKind));
    }

    private bool IsExactFinalizedDelayedPlacementWindow(ProgramCardTriggerWindowFrame window) =>
        (HasMouSimaShi || HasMouHuangQuan || HasMouXuHuangCapability || HasDiouCapability || HasLiuYanCapability || HasBianFuRenCapability || HasCaoChunCapability || HasNeifaCapability || HasSoleTargetProtectionCapability || HasSuFeiCapability || HasJianjiNativeUseCapability || HasFanChouCapability || HasGuoSiCapability) && window.Continuation == ProgramCardContinuation.FinalizedDelayedPlacement &&
        window.TrickContinuation is { TargetCardId: null, RequiredCardKind: null } continuation &&
        window.SimpleContinuation is null && window.CompletedResponseReturn is null && window.AttackOwnerFrameId is null &&
        LifecycleCardUse(window.ParentFrameId) is
            { FinalizedTrickProgramsStarted: true, ProgramUseAccepted: true, Action: { Type: CardActionType.Use } action } use &&
        window.Action.Type == CardActionType.Use && action.ActionId == window.Action.ActionId &&
        action.ActorSeat == use.SourceSeat && action.EffectiveKind == use.CardKind &&
        window.Action.EffectiveKind == use.CardKind && IsDelayedCard(use.CardKind) &&
        continuation.ActionKind == DelayedPlacementActionKind(use.CardKind) &&
        continuation.EffectCardId == use.CardId && (action.PhysicalCards.Any(cost => cost.CardId == use.CardId) || IsDiouUse(use.Id)) &&
        window.Action.PhysicalCards.SequenceEqual(action.PhysicalCards) &&
        action.TargetSeats is [var target] && IsValidPlayerSeat(target) &&
        action.TargetSeats.SequenceEqual(use.TargetSeats);

    private void ContinueFinalizedDelayedPlacement(ProgramCardTriggerWindowFrame completedWindow)
    {
        if (!IsExactFinalizedDelayedPlacementWindow(completedWindow) ||
            _resolutionStack.LastOrDefault()?.Id != completedWindow.ParentFrameId)
            throw new InvalidOperationException("A finalized delayed placement lost its native typed return.");
        var continuation = completedWindow.TrickContinuation!;
        var use = LifecycleCardUse(completedWindow.ParentFrameId)!;
        var action = use.Action!;
        var targetSeat = use.TargetSeats.Single();

        // Quarantine owns the material's outside-game movement and later
        // return. Its exact helper completes this use without placing or
        // discarding those already isolated physical entities.
        if (TryCompleteSoleTargetQuarantinedUse(use.Id)) return;
        var card = GetTrickRepresentation(use.Id, continuation.EffectCardId, requireProcessing: true);
        var target = _players[targetSeat];
        var forbidden = HasCardPolicy(target, SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget);
        if (_winner != Winner.None || IsProgramUseIneffective(use) || IsNamedAmbushUseIneffective(use) || use.IneffectiveTargetSeats?.Contains(targetSeat) == true ||
            !target.IsAlive || target.JudgmentAreaAbolished || forbidden ||
            HasJudgmentEffectiveCard(target, use.CardKind) ||
            use.CardKind != CardKind.Lightning && targetSeat == use.SourceSeat && !IsLiuYanLimuUse(use.Id))
        {
            MarkCardEffectIneffective(use.Id, targetSeat);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, use.SourceSeat,
                targetSeat, use.CardKind, forbidden || use.IneffectiveTargetSeats?.Contains(targetSeat) == true
                    ? CardEffectSkipReason.SkillNullified : CardEffectSkipReason.PublicTargetMissing));
            AddLog("CardEffect", $"【{CardCatalog.Get(use.CardKind).DisplayName}】的目标已不能承接此延时牌，本次放置跳过。",
                use.SourceSeat, targetSeat);
            FinishCardUse(use.Id, card, use.CardKind);
            return;
        }
        BeginJizhiOrNullificationWindow(use.Id, card, action.ActorSeat, use.TargetSeats,
            continuation.ActionKind, playedCardKind: use.CardKind);
    }
}
