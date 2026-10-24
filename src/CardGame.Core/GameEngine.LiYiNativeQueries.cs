namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only an actual weapon in this native payment can make the weapon area empty.
    private int? LiYiPaidSlashWeapon(CharacterState actor, IReadOnlyList<int> materialIds) =>
        actor.IsAlive && HasCardPolicy(actor, SkillProgramCardPolicyKind.EmptyWeaponStoneAxe)
            ? GetEquipment(actor).Where(card => materialIds.Contains(card.Id) &&
                FieldEquipmentSlot(card) == EquipmentSlot.Weapon)
                .Select(card => (int?)card.Id).SingleOrDefault()
            : null;

    private bool HasLiYiWeaponPaymentRange(CharacterState actor, int targetSeat, IReadOnlyList<Card> cards) =>
        LiYiPaidSlashWeapon(actor, cards.Select(card => card.Id).ToArray()) is { } weaponId &&
        HasLiYiVirtualStoneAxe(actor, weaponId) &&
        ConvertRuleValue(EvaluateDistance(actor, _players[targetSeat], weaponId)) <=
        ConvertRuleValue(EvaluateAttackRange(actor, weaponId));

    private bool HasLiYiProspectiveWeaponSlashTarget(CharacterState actor, CharacterState target)
    {
        if (!actor.IsAlive || !HasCardPolicy(actor, SkillProgramCardPolicyKind.EmptyWeaponStoneAxe)) return false;
        foreach (var weapon in GetEquipment(actor).Where(card => FieldEquipmentSlot(card) == EquipmentSlot.Weapon))
            foreach (var kind in SlashKinds)
                foreach (var source in GetProgramViewAsConversions(actor, weapon, kind, forResponse: false))
                    if (CanUseSlashTarget(actor, target, weapon, source, kind, physicalCardIds: [weapon.Id], excludedEquipmentId: weapon.Id))
                        return true;
        return false;
    }
}
