namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool LiuYanLimuMaterialLegal(CharacterState owner,Card card,string skill,string? instance=null)
    {
        var from=_cardZones.GetLocation(card.Id);
        return owner.IsAlive&&from.OwnerSeat==owner.Seat&&from.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)&&
            (from.Zone!=CardZoneKind.WoodenOxGrain||GetPlayableCards(owner).Any(c=>c.Id==card.Id))&&!card.IsGeneralWeapon&&
            !IsActiveProgramSourceEquipmentCard(owner.Seat,skill,instance??GetRuntimeSkillInstanceId(owner,skill),card)&&EffectiveSuit(owner,card)==Suit.Diamond&&
            !IsTurnHandCardRestricted(owner,card)&&!IsPlayPhasePhysicalCardRestricted(owner,card)&&!IsTurnPhysicalUseForbidden(owner.Seat,[card.Id])&&!IsTurnSuitUseForbidden(owner.Seat,Suit.Diamond)&&
            !owner.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(owner,CardKind.Indulgence)&&!HasCardPolicy(owner,SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget)&&
            !IsCardUseForbidden(owner.Seat,CardKind.Indulgence,CardActionType.Use)&&!IsDirectedCardTargetProhibited(owner.Seat,owner.Seat,CardKind.Indulgence)&&
            !IsSelfTargetForbiddenAction(owner,CardKind.Indulgence,[owner.Seat])&&!IsCardTargetProhibited(owner,CardKind.Indulgence,Suit.Diamond,true)&&!HasBeneficiarySuitShield(owner.Seat,owner.Seat,Suit.Diamond);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
    private bool CanActivateLiuYanLimu(CharacterState owner,string skillId)=>GetPlayableCards(owner).Concat(GetEquipment(owner)).Any(c=>LiuYanLimuMaterialLegal(owner,c,skillId));
    private bool HasLiuYanLimuTargetAllowance(CharacterState actor,CharacterState target,int? excludedEquipmentId=null,EquipmentSlot? excludedEquipmentSlot=null)=>
        HasLiuYanCapability&&actor.IsAlive&&target.IsAlive&&actor.Seat!=target.Seat&&HasRuntimeSkill(actor,"ol:limu")&&GetJudgment(actor).Count>0&&
        (IsGameFactionAttackRangeTarget(actor.Seat,target.Seat)||ConvertRuleValue(EvaluateDistance(actor,target,excludedEquipmentId,excludedEquipmentSlot))<=ConvertRuleValue(EvaluateAttackRange(actor,excludedEquipmentId,excludedEquipmentSlot)));
    private bool HasLiuYanLimuQuotaAllowance(CharacterState actor,CharacterState target,Card? physical,IReadOnlyList<int>? ids,int? excludedId,EquipmentSlot? excludedSlot)
    {
        if(!HasLiuYanCapability)return false;
        var materials=(ids??(physical is {Id:>0}?[physical.Id]:[])).ToHashSet();
        var equipment=GetEquipment(actor).Where(c=>materials.Contains(c.Id)).ToArray();
        excludedId??=equipment.FirstOrDefault(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Weapon)?.Id;
        if(excludedSlot is null&&equipment.Any(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.OffensiveHorse))excludedSlot=EquipmentSlot.OffensiveHorse;
        return HasLiuYanLimuTargetAllowance(actor,target,excludedId,excludedSlot);
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 80)]
    private bool DistanceCardUseExemption80HasLiuYanLimuTargetAllowance(ProgramCardDistanceQuery q) =>
        !IsSlashCard(q.Kind) && HasLiuYanLimuTargetAllowance(q.Actor,q.Target,q.ExcludedEquipmentId,q.ExcludedEquipmentSlot);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 80)]
    private bool LiuYanLimuSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasLiuYanLimuQuotaAllowance(query.Actor,query.Target,query.PhysicalCard,query.PhysicalCardIds,query.ExcludedEquipmentId,query.ExcludedEquipmentSlot);
}
