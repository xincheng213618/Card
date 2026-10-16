namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<int> TianChouShandaoTrickTargets(int actor, CardKind kind, IReadOnlyList<int> candidates)
    {
        if (!IsValidPlayerSeat(actor) || !_players[actor].IsAlive || _winner != Winner.None ||
            kind is not (CardKind.FiveGrains or CardKind.ArrowBarrage) ||
            IsCardUseForbidden(actor, kind, CardActionType.Use)) return [];
        var targets = candidates.Distinct().Where(seat => IsValidPlayerSeat(seat) && _players[seat].IsAlive &&
            (kind != CardKind.ArrowBarrage || seat != actor) &&
            (!HasTurnCardTargetRestriction(actor, SkillProgramCardTargetRestriction.SelfOnly) || seat == actor) &&
            !HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ExcludeGlobalTarget, kind) &&
            !IsSelfTargetForbiddenAction(_players[actor], kind, [seat]) &&
            !IsDirectedCardTargetProhibited(actor, seat, kind) &&
            !IsCardTargetProhibited(_players[seat], kind, Suit.None, null) &&
            !HasBeneficiarySuitShield(actor, seat, Suit.None));
        return Array.AsReadOnly(targets.ToArray());
    }

    private void BeginTianChouShandaoTrick(ProgramSkillFrame frame, TianChouShandaoUseOrigin origin)
    {
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || !MatchesTianChouShandao(frame, origin) ||
            origin.CardUseFrameId != _resolutionSequence + 1 || origin.TargetSeats.Count == 0 ||
            !TianChouShandaoTrickTargets(origin.ActorSeat, origin.CardKind, origin.TargetSeats).SequenceEqual(origin.TargetSeats))
            throw new InvalidOperationException("Shandao requires its exact issued global trick and current legal target subset.");
        var card = new Card(0, origin.CardKind, Suit.None, 0);
        var id = BeginCardUse(card, origin.ActorSeat, origin.TargetSeats, origin.CardKind,
            physicalCardIds: [], tianChouShandao: origin);
        if (id != origin.CardUseFrameId)
            throw new InvalidOperationException("Shandao lost its reserved native card-use identity.");
        BeginJizhiOrNullificationWindow(id, card, origin.ActorSeat, LifecycleCardUse(id)!.TargetSeats,
            origin.CardKind == CardKind.FiveGrains ? LegalActionKind.FiveGrains : LegalActionKind.ArrowBarrage,
            requiredCardKind: origin.CardKind == CardKind.ArrowBarrage ? CardKind.Dodge : null,
            playedCardKind: origin.CardKind);
    }

    private bool IsTianChouShandaoUse(long id)
    {
        if (LifecycleCardUse(id) is not { CardId: 0, PhysicalCardIds.Count: 0, TianChouShandao: { } origin,
                Action: { Type: CardActionType.Use, PhysicalCards.Count: 0, ConversionChain.Count: 0,
                    EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false,
                    RequesterSeat: null, ResponderSeat: null, OpponentSeat: null } action } use ||
            origin.CardUseFrameId != id || origin.CardKind is not (CardKind.FiveGrains or CardKind.ArrowBarrage) ||
            use.CardKind != origin.CardKind || action.EffectiveKind != origin.CardKind || action.ProviderSeat != origin.ActorSeat ||
            origin.TargetSeats.Count == 0 || !action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            !MatchesDeclaredActualDamageUse(use, action.ActorSeat, origin.ActorSeat)) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == id);
        return index > 0 && _resolutionStack[index - 1] is ProgramSkillFrame frame && MatchesTianChouShandao(frame, origin);
    }

    private bool MatchesTianChouShandaoUseAction(long id, CardActionContext action, int cardId) =>
        cardId == 0 && IsTianChouShandaoUse(id) && LifecycleCardUse(id)!.Action is { } issued &&
        issued.ActionId == action.ActionId && action.Type == CardActionType.Use &&
        issued.ActorSeat == action.ActorSeat && issued.ProviderSeat == action.ProviderSeat &&
        issued.EffectiveKind == action.EffectiveKind && action.PhysicalCards.Count == 0 && action.ConversionChain.Count == 0 &&
        issued.TargetSeats.SequenceEqual(action.TargetSeats) && action.EffectiveSuit == Suit.None && action.EffectiveRank == 0 &&
        action.EffectiveIsRed == false && action.RequesterSeat is null && action.ResponderSeat is null && action.OpponentSeat is null;

    private bool IsTianChouShandaoAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing) =>
        IsTianChouShandaoUse(attack.ResolutionId) && attack.Card is null && attack.PhysicalCards.Count == 0 &&
        attack.EffectiveCardKind == CardKind.ArrowBarrage && processing.Count == 0 &&
        ActiveGroupCard is { } group && group.ResolutionId == attack.ResolutionId &&
        group.DamageSourceSeat == attack.SourceSeat && SameAttackOwner(group.CurrentAttack, attack);

    [NativeUseValidation(27090)]
    private void ValidateTianChouShandaoUse(CardUseFrame use)
    {
        if (use.TianChouShandao is not null && !IsTianChouShandaoUse(use.Id))
            throw new InvalidOperationException("Shandao lost its native global-trick issuance or owning program.");
    }

    [NativeUseReturn(27090)]
    private void ReturnTianChouShandaoUse(CardUseFrame use)
    {
        if (use.TianChouShandao is not { } origin) return;
        if (use.Step != ResolutionFrameStep.Completed || _resolutionStack.LastOrDefault() is not ProgramSkillFrame frame)
            throw new InvalidOperationException("Shandao requires its actual completed global trick before returning to its program.");
        CompleteTianChouShandaoTrick(frame, origin);
    }

    private void AssertTianChouNativeUses()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.TianChouShandao is not null))
            ValidateTianChouShandaoUse(use);
    }
}
