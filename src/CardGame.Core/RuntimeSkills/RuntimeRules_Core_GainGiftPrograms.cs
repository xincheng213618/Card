using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GainGiftPrograms
{
    internal static bool OwnedLossZone(CardZoneKind zone) => zone is CardZoneKind.Hand or CardZoneKind.Equipment or
        CardZoneKind.Judgment or CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or
        CardZoneKind.Chunlao or CardZoneKind.PojunHold or CardZoneKind.PrivateReserve or CardZoneKind.PublicDeferredPile or
        CardZoneKind.PublicPersistentPile or CardZoneKind.PrivateTurnHold;

    internal static CardConversionSource GainGiftSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);

    internal static Dictionary<string, string> GainGiftParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "gain-after-batch", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
}
