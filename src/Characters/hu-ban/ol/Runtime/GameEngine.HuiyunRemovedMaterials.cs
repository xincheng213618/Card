namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsHuiyunOutsideGeneralWeaponMaterial(long useId, int cardId) =>
        LifecycleCardUse(useId) is { HuiyunOrigin: { } origin } use &&
        origin.Material.CardId == cardId && origin.Material.From.Zone == CardZoneKind.Equipment &&
        GetAdvancedCard(cardId).IsGeneralWeapon && _cardZones.GetLocation(cardId) == CardLocation.OutsideGame &&
        HuiyunUseProof(use);
}
