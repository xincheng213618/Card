using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ShenZhangFeiColorlessUse(CardConversionSource Source,string GameplayHash,long ActionId);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ShenZhangFeiColorlessUse? ShenZhangFeiColorless { get; init; } }
public sealed record ShenZhangFeiColorlessIssuedEvent(long UseId,ShenZhangFeiColorlessUse Receipt) : IGameEvent;
public sealed partial class GameEngine
{
    private bool HasShenZfXunshi(CharacterState p) => HasRuntimeSkill(p,ShenZhangFeiComposition.Xunshi);
    private bool IsShenZfIdentitySource(CharacterState owner,Card card,CardConversionSource? source) =>
        source is { SkillId:ShenZhangFeiComposition.Xunshi,BindingId:"multi-trick-slash" } && source.OwnerSeat == owner.Seat &&
        card.Id > 0 && GetProgramCardIdentityMatches(owner,card).Any(m=>m.Source == source && m.Identity.Id == "multi-trick-slash");
    private bool ShenZfAttackIsColorless(CardAttackHandle attack) =>
        LifecycleCardUse(attack.ResolutionId) is { ShenZhangFeiColorless:{ } r,Action:{ Type:CardActionType.Use } a } u &&
        IsSlashCard(u.CardKind) && attack.EffectiveCardKind == u.CardKind && u.SourceSeat == a.ActorSeat && a.ActorSeat == attack.CardUserSeat &&
        r.ActionId == a.ActionId && r.Source is {SkillId:ShenZhangFeiComposition.Xunshi,BindingId:"colorless-use"} && r.Source.OwnerSeat == a.ActorSeat &&
        !string.IsNullOrWhiteSpace(r.Source.SkillInstanceId) && r.GameplayHash == _contentRegistry.GetSkill(ShenZhangFeiComposition.Xunshi).Program!.GameplayHash &&
        ProgramEventHistory<ShenZhangFeiColorlessIssuedEvent>().Count(e=>e.UseId == u.Id && e.Receipt == r) == 1 && ShenZfColorless(a);
    private Suit? ShenZfIdentitySuit(CharacterState p,Card c) => c.Id > 0 && HasShenZfXunshi(p) && ShenZhangFeiComposition.MultiTricks.Contains(c.Kind) &&
        (_cardZones.GetLocation(c.Id) is { Zone:CardZoneKind.Hand or CardZoneKind.WoodenOxGrain } location && location.OwnerSeat == p.Seat ||
         _cardZones.GetLocation(c.Id) == CardLocation.Processing) ? Suit.None : null;
    private CardSnapshot ProjectShenZfHandCard(CharacterState p,Card c,CardSnapshot snapshot) => ShenZfIdentitySuit(p,c) == Suit.None ? snapshot with { Suit=Suit.None } : snapshot;
    private static bool ShenZfColorless(CardActionContext a) =>
        (a.EffectiveSuit == Suit.None || a.EffectiveIsRed is null && a.EffectiveSuit is null &&
            (a.PhysicalCards.Count == 0 || a.PhysicalCards.Select(c=>c.EffectiveIsRed).Distinct().Count() > 1 || a.PhysicalCards.All(c=>c.EffectiveIsRed is null)));
    private CardActionContext? ShenZfUseAction(ProgramSkillWindowContext w)
    {
        var p = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p=>p.Id == w.ParentFrameId);
        return p is not null && (p.Action.Type == CardActionType.Use || p.CompletedResponseReturn is {IsCommitted:true} typed && IsCompletedResponseUse(p.Action,typed.Kind)) ? p.Action : null;
    }
    private bool ShenZfMaterialsColorless(CharacterState p,IReadOnlyList<int>? ids)
    {
        if (!HasShenZfXunshi(p) || ids is null) return false;
        if (ids.Count == 0) return true;
        var colors = ids.Select(id=>SuitColor(EffectiveSuit(p,GetAdvancedCard(id)))).Distinct().ToArray();
        return colors.Length > 1 || colors is [null];
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,38600)] private void CaptureShenZfColorless(IGameEvent payload)
    {
        if (payload is not CardUseDeclaredEvent e) return;
        if (LifecycleCardUse(e.ResolutionId) is not { Action:{ } a } use || !ShenZfColorless(a) || !HasShenZfXunshi(_players[a.ActorSeat])) return;
        var p = _contentRegistry.GetSkill(ShenZhangFeiComposition.Xunshi).Program!;
        var receipt = new ShenZhangFeiColorlessUse(new(p.Id,"colorless-use",a.ActorSeat,GetRuntimeSkillInstanceId(_players[a.ActorSeat],p.Id)),p.GameplayHash,a.ActionId);
        UpdateLifecycleCardUse(use.Id,u=>u with { ShenZhangFeiColorless=receipt });
        AdvanceEventRulesAndQueueFact(new ShenZhangFeiColorlessIssuedEvent(use.Id,receipt));
    }
    private bool ShenZfIssuedUse(long id,int actor) => LifecycleCardUse(id) is { Action:{ } a } u && a.ActorSeat == actor && ShenZfColorless(a) &&
        (u.ShenZhangFeiColorless is { } r && r.ActionId == a.ActionId && r.Source.OwnerSeat == actor && r.GameplayHash == _contentRegistry.GetSkill(ShenZhangFeiComposition.Xunshi).Program!.GameplayHash &&
         ProgramEventHistory<ShenZhangFeiColorlessIssuedEvent>().Count(e=>e.UseId == id && e.Receipt == r) == 1 || HasShenZfXunshi(_players[actor]));
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption,38600)] private bool ShenZfCardDistance(ProgramCardDistanceQuery q) =>
        q.CardUseFrameId is { } id ? ShenZfIssuedUse(id,q.Actor.Seat) : ShenZfMaterialsColorless(q.Actor,q.PhysicalCardIds);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash,38600)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption,38600)]
    private bool ShenZfSlashDistance(ProgramSlashDistanceQuery q) => q.ExistingUseFrameId is { } id ? ShenZfIssuedUse(id,q.Actor.Seat) :
        ShenZfMaterialsColorless(q.Actor,q.PhysicalCardIds ?? (q.PhysicalCard is { Id:>0 } card ? [card.Id] : q.PhysicalSuit == Suit.None ? [] : null));
    [ProgramDistanceRule(ProgramDistanceStage.ConvertedSlashExemption,38600)] private bool ShenZfConvertedDistance(ProgramSlashConversionDistanceQuery q) =>
        q.Source.SkillId == ShenZhangFeiComposition.Xunshi && q.Source.BindingId == "multi-trick-slash" && q.Source.OwnerSeat == q.Owner.Seat && HasRuntimeSkillInstance(q.Owner,q.Source.SkillId,q.Source.SkillInstanceId);
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit,38600)]
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit,38600)] private bool ShenZfSlashQuota(ProgramSlashQuotaQuery q) =>
        ShenZfMaterialsColorless(q.Actor,q.PhysicalCardIds ?? (q.PhysicalCard is { Id:>0 } card ? [card.Id] : null));
    [ProgramTargetRule(ProgramTargetRulePoint.MaterialPrivilegeAllowance,38600)] private bool ShenZfMaterialPrivilege(ProgramNativeUseMaterialQuery q) =>
        q.ExistingUseFrameId is { } id ? ShenZfIssuedUse(id,q.ActorSeat) : ShenZfMaterialsColorless(_players[q.ActorSeat],q.PhysicalCardIds);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit,38600)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity,38600)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption,38600)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit,38600)] private bool ShenZfQuotaPayment(long id,int actor) => ShenZfIssuedUse(id,actor);
    [ProgramTargetRule(ProgramTargetRulePoint.ExtraSlashTargets,38600)] private ProgramSlashTargetExpansion? ShenZfSlashTargets(ProgramSlashTargetExpansionQuery q)
    {
        if (!IsSlashCard(q.Kind) || !HasShenZfXunshi(_players[q.ActorSeat]) || q.EffectiveSuit != Suit.None && (q.EffectiveIsRed is not null || q.EffectiveSuit is Suit.Heart or Suit.Diamond or Suit.Spade or Suit.Club)) return null;
        var p = _contentRegistry.GetSkill(ShenZhangFeiComposition.Xunshi).Program!;
        var source = new CardUseEffectSource(p.Id,"colorless-use",q.ActorSeat,GetRuntimeSkillInstanceId(_players[q.ActorSeat],p.Id));
        return new(source,p.GameplayHash) { TargetSeats=q.LegalTargetSeats.Where(t=>t!=q.ActorSeat).ToArray() };
    }
    [ProgramTargetRule(ProgramTargetRulePoint.ExtraSlashTargetSource,38600)] private bool ShenZfSlashTargetSource(ProgramSlashTargetSourceQuery q) =>
        q.Source.SkillId == ShenZhangFeiComposition.Xunshi && q.Source.BindingId == "colorless-use" && IsValidPlayerSeat(q.Source.OwnerSeat) &&
        !string.IsNullOrWhiteSpace(q.Source.SkillInstanceId) && q.GameplayHash == _contentRegistry.GetSkill(ShenZhangFeiComposition.Xunshi).Program!.GameplayHash;
    private bool ShenZfCannotRespond(int source,int responder,CardKind incoming,IReadOnlyList<int> targets) =>
        IsSlashCard(incoming) && targets.Contains(responder) && ShenZfMark(responder,PlayerMarkerKind.ShencaiZhang)>0 && ShenZfIssued(responder);
    private IEnumerable<RuleQueryContribution> ShenZfHandLimitContributions(CharacterState p)
    {
        if (p.IsAlive && ShenZfMark(p.Seat,PlayerMarkerKind.ShencaiSi) is >0 and var count && ShenZfIssued(p.Seat))
            yield return new FiniteRuleQueryContribution($"issued:shencai-si:{p.Seat}",SkillRuleOperation.Add,-count);
    }
}
