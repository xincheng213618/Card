namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool UsesSequentialTrickTargets(LegalActionKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_SequentialTrickTargets.UsesSequentialTrickTargets(kind);

    private IReadOnlyList<int> FreezeSequentialTrickTarget(long frameId, IReadOnlyList<int> targets,
        LegalActionKind kind, int? firstTargetCardId, CardKind? requiredCardKind)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame use)
            throw new InvalidOperationException("A trick target window lost its card use.");
        if (use.SequentialTrick is null && targets.Count > 1 && UsesSequentialTrickTargets(kind))
        {
            use = use with { SequentialTrick = new(kind, firstTargetCardId, requiredCardKind) };
            ReplaceRuntimeFrame(_resolutionStack[index].Id, use);
        }
        return use.SequentialTrick is null ? targets : [LeChengyanNativeTargets(use)[use.TargetIndex]];
    }

    private bool HasRemainingSequentialTrickTargets(long frameId) => _winner == Winner.None &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is
            { SequentialTrick: not null } use &&
        LeChengyanNativeTargets(use).Skip(use.TargetIndex + 1).Any(seat => _players[seat].IsAlive);

    private void MoveFinishedTrickCard(long frameId, Card card)
    {
        if (KeepLeChengyanBorrowedMaterial(frameId)) return;
        if (HasUnstartedExtraCardEffect(frameId)) return;
        if (SkipTieredRoundZeroFinishedMovement(frameId, card)) return;
        if (card.Id == 0 && (IsOriginalUseLightning(frameId) || IsZeroEntityUse(frameId) || IsProgramVirtualOrdinaryTrickUse(frameId) || IsJueshengDuelUse(frameId))) return;
        if (IsClaimedUseCardEntity(frameId,card.Id) || (IsHellDestroyedSoulMaterial(frameId, card.Id) || (IsHuiyunOutsideGeneralWeaponMaterial(frameId, card.Id) || IsSiruiOutsideGeneralWeaponMaterial(frameId, card.Id)) || IsGuanduDestroyedChariotMaterial(frameId, card.Id)) || HasRemainingSequentialTrickTargets(frameId) || HasRemainingAdjustedBorrowedSwordTargets(frameId)) return;
        if (LifecycleCardUse(frameId) is { } use && TryFinishAnniversaryXunXuUseMaterials(use)) return;
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.UseFinished);
    }

    private bool TryContinueSequentialTrick(long frameId, bool afterAttack = false)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame { SequentialTrick: { } continuation } use)
            return false;
        var next = use.TargetIndex + 1;
        while (next < LeChengyanNativeTargets(use).Count && !_players[LeChengyanNativeTargets(use)[next]].IsAlive) next++;
        if (_winner != Winner.None || next == LeChengyanNativeTargets(use).Count)
        {
            ReplaceRuntimeFrame(_resolutionStack[index].Id, use with { TargetIndex = LeChengyanNativeTargets(use).Count, SequentialTrick = null });
            return false;
        }
        if (!ReferenceEquals(use, _resolutionStack.LastOrDefault()))
            throw new InvalidOperationException("A trick must finish its child before the next target.");
        var card = IsMouDcZeroUse(use.Id) ? GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true) :
            IsTieredRoundZeroUse(use.Id) ? TieredRoundZeroRepresentation(use.Id) :
            (IsMouZyZeroFireUse(use.Id) || IsProgramVirtualOrdinaryTrickUse(use.Id) || IsDiouZeroUse(use.Id) || IsMouXqJueyanUse(use.Id)) ? GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true) :
            IsMouWenChouZeroDuel(use.Id) || IsMouXuHuangZeroUse(use.Id) || IsJueshengDuelUse(use.Id) ? GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true) :
            (HasSameTypeAidTargetTail(use) || HasUniqueLeaderTrickTargetTail(use) || HasTargetedOrdinaryTrickTail(use) || HasDamageTargetBudgetTail(use) || HasAdjacentRedTargetTail(use)) && use.CardId == 0 && IsIssuedZeroEntityDuel(use.Id)
            ? GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true)
            : (IsClaimedUseCardEntity(use.Id, use.CardId) || (IsHellDestroyedSoulMaterial(use.Id, use.CardId) || (IsHuiyunOutsideGeneralWeaponMaterial(use.Id, use.CardId) || IsSiruiOutsideGeneralWeaponMaterial(use.Id, use.CardId)) || IsGuanduDestroyedChariotMaterial(use.Id, use.CardId))) ? EntityAtCurrentLocation(use.CardId)
            : _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == use.CardId);
        ReplaceRuntimeFrame(_resolutionStack[index].Id, use with { TargetIndex = next, Step = ResolutionFrameStep.ResolvingEffect });
        if (afterAttack)
        {
            ActiveCardAttack = null;
            ActiveDuel = null;
            ClearPendingDecision();
        }
        BeginNullificationWindow(frameId, card, use.SourceSeat, [LeChengyanNativeTargets(use)[next]],
            continuation.ActionKind, null, continuation.RequiredCardKind, use.CardKind);
        return true;
    }

    private void AssertSequentialTrickTargets()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(frame => frame.SequentialTrick is not null))
        {
            if (!UsesSequentialTrickTargets(use.SequentialTrick!.ActionKind) || LeChengyanNativeTargets(use).Count < 2 ||
                LeChengyanNativeTargets(use).Distinct().Count() != LeChengyanNativeTargets(use).Count ||
                LeChengyanNativeTargets(use).Any(seat => !IsValidPlayerSeat(seat)) ||
                use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count ||
                !IsMouDcZeroUse(use.Id) &&
                !IsTieredRoundZeroUse(use.Id) &&
                !IsProgramVirtualOrdinaryTrickUse(use.Id) && !IsDiouZeroUse(use.Id) && !IsMouXqJueyanUse(use.Id) &&
                !IsMouWenChouZeroDuel(use.Id) && !IsMouXuHuangZeroUse(use.Id) && !IsJueshengDuelUse(use.Id) &&
                !(use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && IsIssuedZeroEntityDuel(use.Id) &&
                    (HasSameTypeAidTargetTail(use) || HasUniqueLeaderTrickTargetTail(use) || HasTargetedOrdinaryTrickTail(use) || HasDamageTargetBudgetTail(use) || HasAdjacentRedTargetTail(use))) &&
                _cardZones.GetLocation(use.CardId) != CardLocation.Processing && !IsClaimedUseCardEntity(use.Id, use.CardId) &&
                (!IsHellDestroyedSoulMaterial(use.Id, use.CardId) && !(IsHuiyunOutsideGeneralWeaponMaterial(use.Id, use.CardId) || IsSiruiOutsideGeneralWeaponMaterial(use.Id, use.CardId)) && !IsGuanduDestroyedChariotMaterial(use.Id, use.CardId)))
                throw new InvalidOperationException("A sequential trick lost its targets, cursor or physical card.");
        }
    }
}
