namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ResolveEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        ResolveDynamicEquipmentSkillGrants(card, from, to);
        if (ResolveHulaoEquipmentSkillGrant(card, from, to)) return;
        if (ResolveShenwuEquipmentSkillGrant(card, from, to)) return;
        if (ResolveGuanduEquipmentSkillGrant(card, from, to)) return;
        if (card.Kind != CardKind.XingtianAxe ||
            !_contentRegistry!.Skills.ContainsKey("special:xingtian-axe-effect"))
            return;

        var grantId = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } formerSeat })
            _players[formerSeat].SkillGrants.RemoveGrant(grantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } newSeat })
            _players[newSeat].SkillGrants.Grant(new SkillGrant(
                grantId, "special:xingtian-axe-effect", grantId, grantId));
    }

    private static string? EquipmentSkillGrantId(Card card) => card.Kind switch
    {
        CardKind.XingtianAxe => $"equipment:xingtian:{card.Id}",
        _ => HulaoEquipmentSkillGrantId(card) ?? ShenwuEquipmentSkillGrantId(card) ?? GuanduEquipmentSkillGrantId(card)
    };

    private bool IsActiveProgramSourceEquipmentCard(
        int ownerSeat, string skillId, string skillInstanceId, Card card)
    {
        var grantId = EquipmentSkillGrantId(card);
        return grantId is not null && _players[ownerSeat].SkillGrants.Grants.Any(grant =>
            grant.IsEnabled && grant.GrantId == grantId && grant.SkillId == skillId &&
            grant.SkillInstanceId == skillInstanceId);
    }
}
