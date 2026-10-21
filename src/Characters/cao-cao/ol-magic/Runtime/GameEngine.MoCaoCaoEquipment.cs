namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCardLegality(ProgramCardLegalityStage.LegalActionProjection,33800)]
    private LegalAction? MoCaoCaoEquipmentPhysicalBan(ProgramLegalActionQuery q) => q.Action.Kind==LegalActionKind.Equip &&
        q.Action.CardId is >0 && MoCaoCaoEquipment.IsRestrictedPhysicalEquipment(q.Actor.Seat,q.Action.CardId.Value) ? null : q.Action;
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,33800)]
    private bool MoCaoCaoEquipmentViewAsBan(ProgramViewAsLegalityQuery q) => q.CardIds.Any(id=>MoCaoCaoEquipment.IsRestrictedPhysicalEquipment(q.Owner.Seat,id)) &&
        (q.ForResponse || q.Source.SkillId!=MoCaoCaoComposition.Fuzai || q.Kind is not (CardKind.DrawTwo or CardKind.BorrowedSword));
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan,33800)]
    private bool MoCaoCaoEquipmentResponseBan(ProgramNativeResponseQuery q) => q.CardIds.Any(id=>MoCaoCaoEquipment.IsRestrictedPhysicalEquipment(q.Provider.Seat,id));
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation,33800)]
    private void MoCaoCaoEquipmentSubmitted(ProgramSubmittedUseQuery q)
    {
        if(q.CardIds.Any(id=>MoCaoCaoEquipment.IsRestrictedPhysicalEquipment(q.ActorSeat,id)) &&
            (q.Kind is not (CardKind.DrawTwo or CardKind.BorrowedSword) || !q.ConversionChain.Any(s=>s.SkillId==MoCaoCaoComposition.Fuzai && s.OwnerSeat==q.ActorSeat)))
            throw new InvalidOperationException("Fuzai equipment can only be used through its native Borrowed Sword/Draw Two conversion.");
    }
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeBaseTerms,33800)]
    private void MoCaoCaoVirtualWeaponRange(ProgramAttackRangeQuery q,List<RuleQueryBaseTerm> terms)
    {
        if(q.ExcludedEquipmentSlot!=EquipmentSlot.Weapon && MoCaoCaoEquipment.VirtualKind(q.Player.Seat,EquipmentSlot.Weapon) is { } weapon &&
            EquipmentCatalog.Get(weapon).WeaponAttackRange is { } range && terms[0].Value<range)
            terms[0]=new RuleQueryBaseTerm($"skill:{q.Player.Seat}:mo-fuzai:{weapon}:attack-range",range);
    }
    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption,33800)]
    private bool MoCaoCaoHandDistance(ProgramCardDistanceQuery q) => q.CardUseFrameId is { } id ?
        MoCaoCaoEquipment.HasUsePrivilege(id,q.Actor.Seat) : MoCaoCaoEquipment.HasHandPrivilege(q.Actor.Seat,q.PhysicalCardIds);
    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash,33800)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption,33800)]
    private bool MoCaoCaoHandSlashDistance(ProgramSlashDistanceQuery q) => q.ExistingUseFrameId is { } use ? MoCaoCaoEquipment.HasUsePrivilege(use,q.Actor.Seat) :
        MoCaoCaoEquipment.HasHandPrivilege(q.Actor.Seat,q.PhysicalCardIds ?? (q.PhysicalCard is { Id:>0 } c ? [c.Id] : []));
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit,33800)]
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit,33800)]
    private bool MoCaoCaoHandSlashQuota(ProgramSlashQuotaQuery q) =>
        MoCaoCaoEquipment.HasHandPrivilege(q.Actor.Seat,q.PhysicalCardIds ?? (q.PhysicalCard is { Id:>0 } c ? [c.Id] : []));
    [ProgramTargetRule(ProgramTargetRulePoint.MaterialPrivilegeAllowance,33800)]
    private bool MoCaoCaoHandMaterialPrivilege(ProgramNativeUseMaterialQuery q) => q.ExistingUseFrameId is { } id ?
        MoCaoCaoEquipment.HasUsePrivilege(id,q.ActorSeat) : MoCaoCaoEquipment.HasHandPrivilege(q.ActorSeat,q.PhysicalCardIds);
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeSlashDebit,33800)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AfterSlashDebitIdentity,33800)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashCountExemption,33800)]
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.BeforeAlcoholDebit,33800)]
    private bool MoCaoCaoIssuedHandUse(long id,int actor) => MoCaoCaoEquipment.HasUsePrivilege(id,actor);
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount,33800)]
    private int MoCaoCaoQiBaoDamage(IDamageAttempt attack) =>
        !attack.IsChainPropagation && !attack.IsSourceLess && attack.EffectiveCardKind is { } kind && IsSlashCard(kind) &&
        attack.CardUserSeat==attack.SourceSeat && HasWeaponAbility(_players[attack.SourceSeat],CardKind.QiBaoDao) &&
        _players[attack.TargetSeat].HasHpValue && _players[attack.TargetSeat].Hp==_players[attack.TargetSeat].MaxHp ? 1 : 0;
    private string? MoCaoCaoEquipmentDescription(int seat) =>
        MoCaoCaoEquipment.VirtualKind(seat,EquipmentSlot.Weapon) is { } weapon && MoCaoCaoEquipment.VirtualKind(seat,EquipmentSlot.Armor) is { } armor ?
            $"视为装备【{EquipmentCatalog.Get(weapon).DisplayName}】、【{EquipmentCatalog.Get(armor).DisplayName}】。真实装备区非空时暂停；体力变化重新签发。" : null;
    private bool MoCaoCaoHasHandPrivilege(int seat) => GetHand(_players[seat]).Any(c=>MoCaoCaoEquipment.HasHandPrivilege(seat,[c.Id]));
}
