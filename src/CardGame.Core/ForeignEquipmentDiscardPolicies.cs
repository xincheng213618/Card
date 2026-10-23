namespace CardGame.Core;

internal enum OwnedCardMoveIntent { Discard, Obtain, Transfer, Replacement, Cleanup }

public sealed partial class GameEngine
{
    private bool HasForeignDiscardCapability =>
        HasBianFuRenCapability || HasDagongCheCapability ||
        _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard) ||
        _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.XiaHuiBlackHandAndGainedEntityRestrictions) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.SelectEquipmentPairAndPayment) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SelectDyingOwnedCard) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RestrictDamageSourceHandCategory);
    // Intent and actor come from the actual operation, never the movement reason
    // or the current turn. This is a pure, opt-in pre-movement eligibility query.
    private bool IsForeignEquipmentDiscardPrevented(int actorSeat, Card card, CardLocation from,
        OwnedCardMoveIntent intent)
    {
        if (IsSelfHandCategoryDiscardForbidden(actorSeat, card, from, intent)) return true;
        if (intent != OwnedCardMoveIntent.Discard || from.Zone != CardZoneKind.Equipment ||
            from.OwnerSeat is not { } ownerSeat || actorSeat == ownerSeat ||
            !_players[ownerSeat].IsAlive || _cardZones.GetLocation(card.Id) != from ||
            !EquipmentCatalog.IsEquipment(card.Kind) ||
            EquipmentCatalog.Get(card.Kind).Slot is not (EquipmentSlot.Armor or EquipmentSlot.Treasure)) return false;
        return HasCardPolicy(_players[ownerSeat], SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard);
    }

    private bool HasDiscardableHeBy(int actorSeat, int ownerSeat) =>
        GetHand(_players[ownerSeat]).Any(card => !IsSelfHandCategoryDiscardForbidden(actorSeat, card, CardLocation.Hand(ownerSeat), OwnedCardMoveIntent.Discard)) || GetEquipment(ownerSeat)
            .Any(card => !IsForeignEquipmentDiscardPrevented(actorSeat, card,
                CardLocation.Equipment(ownerSeat), OwnedCardMoveIntent.Discard));
}
