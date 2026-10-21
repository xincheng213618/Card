namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ShenDaHasMaterialDomain(int actor, IReadOnlyList<int>? ids, ShenDengAiDomain domain) =>
        ids is { Count: > 0 } && HasRuntimeSkill(_players[actor], ShenDaTuoYu) &&
        ids.Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(actor) && ShenDaAssignment(actor, id)?.Domain == domain);
    private bool ShenDaHasIssuedDomain(long? id, int actor, ShenDengAiDomain domain) => id is { } useId &&
        LifecycleCardUse(useId) is { ShenDengAiDomain: { } r, Action: { } a } && r.ActorSeat == actor &&
        r.ActionId == a.ActionId && (r.DomainMask & (1 << (int)domain)) != 0 &&
        ProgramEventHistory<ShenDengAiDomainIssuedEvent>().Count(e => e.UseId == useId && e.ActionId == r.ActionId && e.ActorSeat == actor && e.DomainMask == r.DomainMask) == 1;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 38500)]
    private void IssueShenDengAiDomains(CardUseDeclaredEvent declared)
    {
        if (LifecycleCardUse(declared.ResolutionId) is not { Action: { Type: CardActionType.Use, PhysicalCards.Count: > 0 } a } use ||
            a.ActorSeat != a.ProviderSeat || !HasRuntimeSkill(_players[a.ActorSeat], ShenDaTuoYu)) return;
        var domains = a.PhysicalCards.Where(c => c.From == CardLocation.Hand(a.ActorSeat)).Select(c => ShenDaAssignment(a.ActorSeat, c.CardId, true)?.Domain)
            .Where(d => d is not null).Select(d => d!.Value).Distinct().ToArray();
        if (domains.Length == 0) return;
        var mask = domains.Aggregate(0, (n, d) => n | (1 << (int)d));
        var noResponse = use.IssuedNoResponse;
        if (domains.Contains(ShenDengAiDomain.Junshan) && noResponse is null)
            noResponse = new(a.ActionId, use.Id, new(ShenDaTuoYu, "junshan-entity", a.ActorSeat, GetRuntimeSkillInstanceId(_players[a.ActorSeat], ShenDaTuoYu)), _turnNumber, _cardUseDebitPhaseInstanceId);
        UpdateLifecycleCardUse(use.Id, u => u with { ShenDengAiDomain = new(a.ActionId, a.ActorSeat, mask), IssuedNoResponse = noResponse });
        AdvanceEventRulesAndQueueFact(new ShenDengAiDomainIssuedEvent(use.Id, a.ActionId, a.ActorSeat, mask));
        if (noResponse is not null && use.IssuedNoResponse is null) AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(noResponse));
    }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 38500)]
    private bool ShenDaCardDistance(ProgramCardDistanceQuery q) => q.CardUseFrameId is { } id ?
        ShenDaHasIssuedDomain(id, q.Actor.Seat, ShenDengAiDomain.Qingqu) : ShenDaHasMaterialDomain(q.Actor.Seat, q.PhysicalCardIds, ShenDengAiDomain.Qingqu);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 38500)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 38500)]
    private bool ShenDaSlashDistance(ProgramSlashDistanceQuery q) => q.ExistingUseFrameId is { } id ?
        ShenDaHasIssuedDomain(id, q.Actor.Seat, ShenDengAiDomain.Qingqu) :
        ShenDaHasMaterialDomain(q.Actor.Seat, q.PhysicalCardIds ?? (q.PhysicalCard is { Id: > 0 } card ? [card.Id] : []), ShenDengAiDomain.Qingqu);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 38500)]
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 38500)]
    private bool ShenDaSlashQuota(ProgramSlashQuotaQuery q) => ShenDaHasMaterialDomain(q.Actor.Seat,
        q.PhysicalCardIds ?? (q.PhysicalCard is { Id: > 0 } card ? [card.Id] : []), ShenDengAiDomain.Qingqu);
    [ProgramTargetRule(ProgramTargetRulePoint.MaterialPrivilegeAllowance, 38500)]
    private bool ShenDaMaterialPrivilege(ProgramNativeUseMaterialQuery q) => q.ExistingUseFrameId is { } id ?
        ShenDaHasIssuedDomain(id, q.ActorSeat, ShenDengAiDomain.Qingqu) : ShenDaHasMaterialDomain(q.ActorSeat, q.PhysicalCardIds, ShenDengAiDomain.Qingqu);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit, 38500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity, 38500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 38500)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 38500)]
    private bool ShenDaIssuedQuota(long id, int actor) => ShenDaHasIssuedDomain(id, actor, ShenDengAiDomain.Qingqu);
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 38500)]
    private int ShenDaFengtianDamage(IDamageAttempt a) => !a.IsChainPropagation && !a.IsSourceLess &&
        ShenDaHasIssuedDomain(a.ResolutionId, a.CardUserSeat, ShenDengAiDomain.Fengtian) ? 1 : 0;
    private int ApplyShenDengAiFengtianRecovery(long id, int actor, int target, int amount) =>
        ShenDaHasIssuedDomain(id, actor, ShenDengAiDomain.Fengtian) ? checked(amount + 1) : amount;
}
