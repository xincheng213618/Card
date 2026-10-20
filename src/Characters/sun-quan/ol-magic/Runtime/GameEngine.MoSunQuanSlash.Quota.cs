namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMoQuanyuProspectiveQuota(CharacterState actor, CharacterState target, CardKind kind) =>
        TryGetMoQuanyuDeclarationQuota(actor, target, kind, out var frozen) ? frozen :
        HasMoQuanyuSlash && IsSlashCard(kind) && actor.IsAlive && target.IsAlive && actor.Seat != target.Seat &&
        TryGetMoQuanyuSource(actor.Seat, out _, out _) &&
        (MoQuanyuSlashMask(actor.Seat, target.Seat) & MoQuanyuBit(MoQuanyuEffect.Liuxing)) != 0;

    private LegalAction? RestrictMoQuanyuQuotaAction(CharacterState actor, LegalAction action, CardKind kind, Card? nativeOriginal)
    {
        if (!HasMoQuanyuSlash || !TryGetMoQuanyuSource(actor.Seat, out _, out _) || action.Kind != LegalActionKind.Slash || !IsSlashCard(kind) ||
            _phase != TurnPhase.Play || actor.Seat != _currentSeat) return action;
        var targets = action.TargetSeats.Count > 0 ? action.TargetSeats : action.TargetSeat is { } t ? [t] : Array.Empty<int>();
        if (targets.Count == 0) return action;
        var card = nativeOriginal ?? (action.CardId is { } id && id != 0 ? GetPlayableCards(actor).SingleOrDefault(c => c.Id == id) : null);
        var ids = action.CardId is { } physical && physical != 0 ? new[] { physical } : Array.Empty<int>();
        if (card is not null)
            card = WuJingLiubingUseCard(actor, ApplyProgramUseAppearance(actor, card, action.ConversionSource),
                kind, action.ConversionSource, ids);
        // Test the existing quota paths without this target-dependent allowance.
        // An independent unlimited-use policy may still authorize a multi-target Slash.
        var ordinarilyLegal = targets.All(t => CanSpendSlashUse(actor, _players[t],
            IgnoresProgramSlashLimit(actor, action.ConversionSource), kind, card,
            physicalCardIds: ids, ignoreMoQuanyu: true));
        if (ordinarilyLegal) return action;
        if (targets.Count != 1 || !HasMoQuanyuProspectiveQuota(actor, _players[targets[0]], kind)) return null;
        return action.MaxTargetCount > 1 ? action with { MaxTargetCount = 1 } : action;
    }

    // Complete selected targets are checked together: Meteor authorizes an
    // initial singleton, never each member of an otherwise unpaid multi-Slash.
    private bool IsMoQuanyuCompleteZhangbaQuotaLegal(CharacterState actor, IReadOnlyList<int> targets,
        IReadOnlyList<Card> cards) => !HasMoQuanyuSlash || !TryGetMoQuanyuSource(actor.Seat, out _, out _) || targets.Count <= 1 ||
        targets.All(t => IsValidPlayerSeat(t) && CanSpendSlashUse(actor, _players[t],
            ignoresCount: HasPhaseSuitAllowance(actor.Seat, PhysicalGroupSuit(actor, cards)),
            CardKind.Slash, effectiveRank: ZhangbaSpecificSlashRank(actor, cards),
            physicalCardIds: cards.Select(c => c.Id).ToArray(), ignoreMoQuanyu: true));

    private LegalAction RestrictMoQuanyuZhangbaQuotaAction(CharacterState actor, LegalAction action)
    {
        if (!HasMoQuanyuSlash || !TryGetMoQuanyuSource(actor.Seat, out _, out _) || action is not { Kind: LegalActionKind.UseEquipmentEffect,
                EquipmentKind: CardKind.ZhangbaSerpentSpear, MaxTargetCount: > 1 } ||
            _phase != TurnPhase.Play || actor.Seat != _currentSeat) return action;
        var maximum = GetZhangbaSlashPairs(actor).Select(cards =>
        {
            var ids = cards.Select(c => c.Id).ToArray();
            var suit = PhysicalGroupSuit(actor, cards); var rank = ZhangbaSpecificSlashRank(actor, cards);
            return action.SelectableTargetSeats.Count(t => IsValidPlayerSeat(t) &&
                CanUseVirtualSlashTarget(actor, _players[t], CardKind.Slash, suit,
                    PhysicalGroupColor(actor, cards), rank, ids) &&
                CanSpendSlashUse(actor, _players[t], ignoresCount: HasPhaseSuitAllowance(actor.Seat, suit),
                    CardKind.Slash, effectiveRank: rank, physicalCardIds: ids, ignoreMoQuanyu: true));
        }).DefaultIfEmpty(0).Max();
        return action with { MaxTargetCount = Math.Min(action.MaxTargetCount, Math.Max(1, maximum)) };
    }

    private void CaptureMoQuanyuQuota(CardUseFrame use)
    {
        if (TryInheritMoQuanyuDeclarationQuota(use)) return;
        if (!HasMoQuanyuSlash || use.Action is not { Type: CardActionType.Use } a || !IsSlashCard(use.CardKind) ||
            use.TargetSeats is not [var target] || !HasMoQuanyuProspectiveQuota(_players[use.SourceSeat], _players[target], use.CardKind) ||
            !TryGetMoQuanyuSource(use.SourceSeat, out var source, out var hash)) return;
        var r = new MoQuanyuQuotaReceipt(use.Id, a.ActionId, use.SourceSeat, target, use.CardKind,
            source, hash, MoQuanyuSlashMask(use.SourceSeat, target), MoQiangangEntered(use.SourceSeat));
        UpdateLifecycleCardUse(use.Id, current => current with { MoQuanyuQuota = r });
        AdvanceEventRulesAndQueueFact(new MoQuanyuQuotaAcceptedEvent(r));
    }

    [ProgramCardLegality(ProgramCardLegalityStage.LegalActionProjection, 10)]
    private LegalAction? ProjectMoQuanyuLegalAction(ProgramLegalActionQuery query) =>
        query.Action is { Kind: LegalActionKind.UseEquipmentEffect, EquipmentKind: CardKind.ZhangbaSerpentSpear }
            ? RestrictMoQuanyuZhangbaQuotaAction(query.Actor, query.Action)
            : TryGetLegalActionEffectiveCardKind(query.Actor, query.Action, out var kind, query.NativeOriginalCard)
                ? RestrictMoQuanyuQuotaAction(query.Actor, query.Action, kind, query.NativeOriginalCard) : query.Action;
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 120)]
    private bool MoQuanyuSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        !query.IgnoreMoQuanyu && HasMoQuanyuProspectiveQuota(query.Actor, query.Target, query.Kind);
}
