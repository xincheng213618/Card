namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMobileShiZhuangshiDistance(int seat, long? existingUse = null)
    {
        if (existingUse is { } id && LifecycleCardUse(id)?.Action is { } action)
            return ProgramEventHistory<MobileShiWeiYanUseEvent>().Any(e => e.UseId == id && e.ActionId == action.ActionId &&
                e.OwnerSeat == seat && e.DistanceAndNoResponse);
        return ShiWyPhaseGrant(seat) is { } grant && ShiWyPhaseUses(seat, grant.TurnNumber, grant.PhaseInstanceId) < grant.DiscardedCards;
    }
    private bool HasMobileShiZhuangshiQuota(int seat) => ShiWyPhaseGrant(seat) is { } grant &&
        ShiWyPhaseUses(seat, grant.TurnNumber, grant.PhaseInstanceId) < grant.LostHp;
    private CardConversionSource? MobileShiZhuangshiCounterspellSource(CharacterState owner, IReadOnlyList<int> physicalCardIds) =>
        HasMobileShiZhuangshiDistance(owner.Seat) ? ShiWyPhaseGrant(owner.Seat)?.Source : null;
    private bool IsMobileShiZhuangshiUnrespondable(long id) =>
        LifecycleCardUse(id) is { Action: { } action } && ProgramEventHistory<MobileShiWeiYanUseEvent>()
            .Any(e => e.UseId == id && e.ActionId == action.ActionId && e.DistanceAndNoResponse);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit, 47100)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption, 47100)]
    private bool IsIssuedMobileShiZhuangshiNoCount(long id, int actor) =>
        LifecycleCardUse(id) is { Action: { } action } && ProgramEventHistory<MobileShiWeiYanUseEvent>()
            .Any(e => e.UseId == id && e.ActionId == action.ActionId && e.OwnerSeat == actor && e.NoCount);
    private int MobileShiWeiYanPhaseSlashBonus(int seat) => _phase == TurnPhase.Play && seat == _currentSeat
        ? ProgramEventHistory<MobileShiKuangguSlashGrantedEvent>().Count(e => e.OwnerSeat == seat &&
            e.TurnNumber == _turnNumber && e.PhaseInstanceId == _cardUseDebitPhaseInstanceId) : 0;

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 47100)]
    private bool MobileShiZhuangshiCardDistance(ProgramCardDistanceQuery q) => HasMobileShiZhuangshiDistance(q.Actor.Seat, q.CardUseFrameId);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 47100)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 47100)]
    private bool MobileShiZhuangshiSlashDistance(ProgramSlashDistanceQuery q) => HasMobileShiZhuangshiDistance(q.Actor.Seat, q.ExistingUseFrameId);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 47100)]
    private bool MobileShiZhuangshiSlashQuota(ProgramSlashQuotaQuery q) => HasMobileShiZhuangshiQuota(q.Actor.Seat);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 47100)]
    private void CaptureMobileShiWeiYanUse(IGameEvent payload)
    {
        if (_phase != TurnPhase.Play || _currentSeat != _turnProgression.OwnerSeat) return;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { } use &&
            use.SourceSeat == _currentSeat && use.Action is { Type: CardActionType.Use } action)
        {
            var grant = ShiWyPhaseGrant(use.SourceSeat);
            if (grant is null || ProgramEventHistory<MobileShiWeiYanUseEvent>().Any(e => e.ActionId == action.ActionId)) return;
            var ordinal = ShiWyPhaseUses(use.SourceSeat, grant.TurnNumber, grant.PhaseInstanceId) + 1;
            var noCount = ordinal <= grant.LostHp;
            if (noCount)
            {
                TryMarkUnlimitedUse(use.Id);
                UpdateLifecycleCardUse(use.Id, f => f with { QuotaOnlyUseLimitExemption = true });
            }
            AdvanceEventRulesAndQueueFact(new MobileShiWeiYanUseEvent(use.Id, action.ActionId, use.SourceSeat,
                _turnNumber, _cardUseDebitPhaseInstanceId, ordinal, ordinal <= grant.DiscardedCards, noCount));
        }
        else if (payload is CardActionAcceptedEvent { Action.Type: CardActionType.Response } response)
        {
            var a = response.Action;
            if (a.ActorSeat != _currentSeat || a.ProviderSeat != a.ActorSeat || a.RequesterSeat is not null ||
                !(a.EffectiveKind == CardKind.Nullification || a.EffectiveKind == CardKind.Dodge && IsProgramResponseCardUse(_players[a.ActorSeat], a.EffectiveKind)) ||
                ShiWyPhaseGrant(a.ActorSeat) is not { } grant || ProgramEventHistory<MobileShiWeiYanUseEvent>().Any(e => e.ActionId == a.ActionId)) return;
            var ordinal = ShiWyPhaseUses(a.ActorSeat, grant.TurnNumber, grant.PhaseInstanceId) + 1;
            var owner = a.EffectiveKind == CardKind.Nullification ? ActiveNullificationWindow?.Id ?? 0 : ActiveCardAttack?.ResolutionId ?? 0;
            AdvanceEventRulesAndQueueFact(new MobileShiWeiYanUseEvent(owner, a.ActionId, a.ActorSeat, _turnNumber,
                _cardUseDebitPhaseInstanceId, ordinal, ordinal <= grant.DiscardedCards, ordinal <= grant.LostHp));
        }
    }

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 47100)]
    private int MobileShiYinzhanDamageBonus(IDamageAttempt attack)
    {
        if (attack.IsSourceLess || attack.CardUserSeat != attack.SourceSeat || !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Duel) ||
            !IsValidPlayerSeat(attack.SourceSeat) || !HasProgramSkill(_players[attack.SourceSeat], ShiWyYinzhan) ||
            LifecycleCardUse(attack.ResolutionId) is not { CardKind: CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash }) return 0;
        var source = _players[attack.SourceSeat]; var target = _players[attack.TargetSeat];
        var hp = source.HasHpValue && target.HasHpValue && source.Hp <= target.Hp;
        var hand = GetHand(source).Count <= GetHand(target).Count;
        AdvanceEventRulesAndQueueFact(new MobileShiYinzhanQualifiedEvent(++_resolutionSequence, attack.ResolutionId, source.Seat, target.Seat, hp, hand));
        return !attack.IsChainPropagation && hp ? 1 : 0;
    }
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 47101)]
    private void CaptureMobileShiYinzhanApplied(DamageAppliedEvent damage)
    {
        if (damage.SourceCardUseResolutionId is not { } useId || damage.Amount <= 0 || damage.SourceLess) return;
        var qualification = ProgramEventHistory<MobileShiYinzhanQualifiedEvent>().LastOrDefault(e => e.UseId == useId &&
            e.OwnerSeat == damage.SourceSeat && e.TargetSeat == damage.TargetSeat &&
            !ProgramEventHistory<MobileShiYinzhanAppliedEvent>().Any(a => a.QualificationId == e.Id));
        if (qualification is not null) AdvanceEventRulesAndQueueFact(new MobileShiYinzhanAppliedEvent(qualification.Id));
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiWeiYanPublicStates(CharacterState owner, string skillId)
    {
        var states = new List<ProgramPublicRuleStateSnapshot>();
        if (skillId == ShiWyZhuangshi && ShiWyPhaseGrant(owner.Seat) is { } grant)
        {
            var instance = GetSkillBindingShard(owner).ProgramInstances.FirstOrDefault(i => i.SkillId == skillId);
            if (instance is not null)
            {
                var used = ShiWyPhaseUses(owner.Seat, grant.TurnNumber, grant.PhaseInstanceId);
                states.Add(new(instance.SkillInstanceId, "zhuangshi-discard", ProgramPublicRuleStateKind.ActivationLimit, grant.DiscardedCards, Math.Min(used, grant.DiscardedCards), SkillUsageScope.Phase));
                states.Add(new(instance.SkillInstanceId, "zhuangshi-hp", ProgramPublicRuleStateKind.ActivationLimit, grant.LostHp, Math.Min(used, grant.LostHp), SkillUsageScope.Phase));
            }
        }
        return Array.AsReadOnly(states.ToArray());
    }
}
