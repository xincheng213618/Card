namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidWuLuKangDixianGrant(WuLuKangDixianGrant g) => g.Rank is >= 1 and <= 13 &&
        IsValidPlayerSeat(g.Source.OwnerSeat) && g.Source.SkillId == WuLuKangComposition.Dixian && g.Source.BindingId == "declare-rank" &&
        _contentRegistry.Skills.GetValueOrDefault(g.Source.SkillId)?.Program is { } p && p.GameplayHash == g.GameplayHash && WuLuKangComposition.IsDixian(p) &&
        ProgramEventHistory<WuLuKangDixianStartedEvent>().Count(e => e.FrameId == g.ProgramFrameId && e.Source == g.Source && e.GameplayHash == g.GameplayHash && e.Phase == g.Phase) == 1 &&
        ProgramEventHistory<WuLuKangDixianRankChosenEvent>().Count(e => e.FrameId == g.ProgramFrameId && e.Rank == g.Rank && e.AllAtLeast && e.MaterialCount == 0) == 1 &&
        ProgramEventHistory<WuLuKangDixianDrawReturnedEvent>().Count(e => e.FrameId == g.ProgramFrameId && e.RequestedCount == g.Rank) == 1 &&
        ProgramEventHistory<WuLuKangDixianGrantedEvent>().Count(e => e.Grant == g) == 1;
    private WuLuKangDixianGrant? WuLuKangDixianGrantFor(int actorSeat) => !HasWuLuKangDixianCapability ? null :
        ProgramEventHistory<WuLuKangDixianGrantedEvent>().Select(e => e.Grant).SingleOrDefault(g => g.Source.OwnerSeat == actorSeat && ValidWuLuKangDixianGrant(g));
    private bool HasWuLuKangDixianGrant(int actorSeat) => WuLuKangDixianGrantFor(actorSeat) is not null;
    private WuLuKangDixianGrant? WuLuKangDixianProspectiveGrantFor(int actorSeat)
    {
        var top = _resolutionStack.LastOrDefault();
        var p = top is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration ? declaration.Payment : top?.AcceptedDeclarationPayment;
        return p is { Claimed: false, WuLuKangDixian: { } r } && r.ActorSeat == actorSeat && ValidWuLuKangDixianDeclaration(r)
            ? r.Grant : WuLuKangDixianGrantFor(actorSeat);
    }
    private bool HasWuLuKangDixianRankPrivilege(int actorSeat, int? rank, long? useId = null) => useId is { } id
        ? HasIssuedWuLuKangDixianUse(id, actorSeat)
        : rank is > 0 && WuLuKangDixianProspectiveGrantFor(actorSeat) is { } g && rank <= g.Rank;
    private int? WuLuKangDixianQueryRank(CharacterState actor, CardKind kind, IReadOnlyList<int>? ids, CardConversionSource? source = null, bool isTrueZhangba = false)
    {
        if (!HasWuLuKangDixianGrant(actor.Seat) || ids is not { Count: > 0 } || ids.Any(id => id <= 0)) return null;
        if (source is not null && IsXieweiSource(source)) return 0;
        var cards = ids.Select(id => GetAdvancedCard(id)).ToArray(); var id = ids[0]; var card = cards[0];
        var provider = CapturedDeclarationOrigin(id, _cardZones.GetLocation(id)).OwnerSeat is { } seat ? _players[seat] : actor;
        if (ids.Count == 1 && UnclaimedDeclarationPayment(provider.Seat, id)?.WuLuKangDixian is { } paid && ValidWuLuKangDixianDeclaration(paid)) return paid.EffectiveRank;
        if ((CaptureJuewuActionRank(provider.Seat, ids) ?? CaptureAlcoholActionRank(provider.Seat, kind, ids)) is { } nativeRank) return nativeRank;
        if (ids.Count != 1) return isTrueZhangba && source is null && IsSlashCard(kind) && ids.Count == 2 && UsesFormalZhangbaSerpentSpear && HasWeaponAbility(provider, CardKind.ZhangbaSerpentSpear)
            ? Math.Min(13, cards.Sum(c => JuewuForeignHandRank(provider, c) ?? c.Rank)) : null;
        // Use the same native appearance transformations as the accepted action.
        // A zero-point conversion stays zero; no printed-rank fallback is used.
        var appearance = ApplyProgramUseAppearance(provider, card, source);
        return appearance.Rank;
    }
    private bool HasWuLuKangDixianMaterialPrivilege(CharacterState actor, CardKind kind, IReadOnlyList<int>? ids, CardConversionSource? source = null) =>
        HasWuLuKangDixianRankPrivilege(actor.Seat, WuLuKangDixianQueryRank(actor, kind, ids, source));
    private string? WuLuKangDixianPublicDescription(int actorSeat) => WuLuKangDixianGrantFor(actorSeat) is { } g
        ? $"本局使用有点数且点数≤{g.Rank}的牌无距离和次数限制；不占通常【杀】和【酒】次数。" : null;
    private bool ValidWuLuKangDixianDeclaration(WuLuKangDixianDeclarationReceipt r) =>
        r.DeclarationId > 0 && IsValidPlayerSeat(r.ActorSeat) && IsValidPlayerSeat(r.ProviderSeat) && r.CardId > 0 && r.EffectiveRank is >= 0 and <= 13 &&
        (r.Grant is null || r.Grant.Source.OwnerSeat == r.ActorSeat && ValidWuLuKangDixianGrant(r.Grant)) &&
        ProgramEventHistory<WuLuKangDixianDeclarationAdmittedEvent>().Count(e => e.Receipt == r) == 1;
    private DeclaredCardPayment CaptureWuLuKangDixianDeclaration(DeclaredCardPayment payment, CardDeclarationParentReturn parent)
    {
        if (!HasWuLuKangDixianCapability || !BeifenDeclarationIsTrueUse(parent) || payment.ActorSeat != parent.ActorSeat ||
            !IsValidPlayerSeat(parent.ActorSeat) || payment.Claimed || payment.WuLuKangDixian is not null || _cardZones.GetLocation(payment.Cost.CardId) != payment.Cost.From) return payment;
        var provider = _players[payment.ProviderSeat]; var card = GetAdvancedCard(payment.Cost.CardId);
        var appearance = ApplyProgramUseAppearance(provider, card, payment.Source);
        var rank = CaptureJuewuActionRank(provider.Seat, [card.Id]) ?? CaptureAlcoholActionRank(provider.Seat, payment.DeclaredKind, [card.Id]) ?? appearance.Rank;
        var r = new WuLuKangDixianDeclarationReceipt(payment.DeclarationId, parent.ActorSeat, provider.Seat, card.Id, payment.DeclaredKind, rank,
            WuLuKangDixianGrantFor(parent.ActorSeat));
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianDeclarationAdmittedEvent(r)); return payment with { WuLuKangDixian = r };
    }
    private int? CaptureWuLuKangDixianDeclarationRank(int provider, IReadOnlyList<int> ids) =>
        HasWuLuKangDixianCapability && ids is [var id] && UnclaimedDeclarationPayment(provider, id)?.WuLuKangDixian is { } r &&
        ValidWuLuKangDixianDeclaration(r) ? r.EffectiveRank : null;
    private void IssueWuLuKangDixianUse(CardUseFrame use)
    {
        if (!HasWuLuKangDixianCapability || use.Action is not { Type: CardActionType.Use } a ||
            use.WuLuKangDixianUse is { } old && old.ActionId == a.ActionId && old.ActorSeat == a.ActorSeat) return;
        var payment = use.AcceptedDeclarationPayment;
        if (payment is null && a.PhysicalCards is [var cost]) payment = UnclaimedDeclarationPayment(a.ProviderSeat, cost.CardId);
        var grant = WuLuKangDixianGrantFor(a.ActorSeat);
        if (payment?.WuLuKangDixian is { } paid && paid.ActorSeat == a.ActorSeat)
        {
            if (!ValidWuLuKangDixianDeclaration(paid) || paid.ProviderSeat != a.ProviderSeat || paid.EffectiveRank != (a.EffectiveRank ?? 0) ||
                a.PhysicalCards is not [var material] || material.CardId != paid.CardId || material.From != payment.Cost.From)
                throw new InvalidOperationException("Dixian lost its prepayment declaration's exact native rank and actor.");
            grant = paid.Grant;
        }
        var r = new WuLuKangDixianUseReceipt(use.Id, a.ActionId, a.ActorSeat, a.ProviderSeat, a.EffectiveKind, a.EffectiveRank,
            grant, grant is not null && a.EffectiveRank is > 0 && a.EffectiveRank <= grant.Rank);
        ReplaceRuntimeFrame(use.Id, use with { WuLuKangDixianUse = r });
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianUseAdmittedEvent(r));
    }
    private bool HasIssuedWuLuKangDixianUse(long useId, int actorSeat) =>
        LifecycleCardUse(useId) is { Action: { Type: CardActionType.Use } a, WuLuKangDixianUse: { Applies: true, Grant: { } g } r } use &&
        r.UseId == use.Id && r.ActionId == a.ActionId && r.ActorSeat == a.ActorSeat && r.ActorSeat == actorSeat && r.ProviderSeat == a.ProviderSeat &&
        r.EffectiveRank is > 0 && r.EffectiveRank <= g.Rank && r.EffectiveRank == a.EffectiveRank && ValidWuLuKangDixianGrant(g) && g.Source.OwnerSeat == actorSeat &&
        (r.Kind == a.EffectiveKind || IsSlashCard(r.Kind) && IsSlashCard(a.EffectiveKind) && IsCurrentSlashFireChangedUse(use)) &&
        ProgramEventHistory<WuLuKangDixianUseAdmittedEvent>().Count(e => e.Receipt == r) == 1;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29701)]
    private void ObserveWuLuKangDixianUse(IGameEvent fact)
    {
        if (!HasWuLuKangDixianCapability) return;
        if (fact is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use) IssueWuLuKangDixianUse(use);
        if (fact is ProgramCardUseActorReplacedEvent replaced && LifecycleCardUse(replaced.CardUseFrameId) is { } reassigned) IssueWuLuKangDixianUse(reassigned);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 29701)]
    private void AssertWuLuKangDixianUses()
    {
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.WuLuKangDixianUse is not null))
        {
            var r = use.WuLuKangDixianUse!;
            if (use.Action is not { Type: CardActionType.Use } a || r.UseId != use.Id || r.ActionId != a.ActionId || r.ActorSeat != a.ActorSeat ||
                r.ProviderSeat != a.ProviderSeat || r.EffectiveRank != a.EffectiveRank ||
                r.Kind != a.EffectiveKind && !(IsSlashCard(r.Kind) && IsSlashCard(a.EffectiveKind) && IsCurrentSlashFireChangedUse(use)) ||
                r.Grant is { } g && (g.Source.OwnerSeat != r.ActorSeat || !ValidWuLuKangDixianGrant(g)) ||
                r.Applies != (r.Grant is { } grant && r.EffectiveRank is > 0 && r.EffectiveRank <= grant.Rank) ||
                ProgramEventHistory<WuLuKangDixianUseAdmittedEvent>().Count(e => e.Receipt == r) != 1)
                throw new InvalidOperationException("Dixian changed its frozen actual Use actor, effective rank or issued permanent admission.");
        }
    }

    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 30)]
    private bool DistanceSpecificSlashExemption30HasWuLuKangDixianRankPrivilege(ProgramSlashDistanceQuery q) =>
        HasWuLuKangDixianRankPrivilege(q.Actor.Seat, q.EffectiveRank, q.ExistingUseFrameId);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 80)]
    private bool DixianRankSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) =>
        HasWuLuKangDixianRankPrivilege(query.Actor.Seat, query.EffectiveRank ?? (query.PhysicalCard is not null ? SpecificSlashRank(query.Actor, query.PhysicalCard, query.Kind) : WuLuKangDixianQueryRank(query.Actor, query.Kind, query.PhysicalCardIds)));
}
