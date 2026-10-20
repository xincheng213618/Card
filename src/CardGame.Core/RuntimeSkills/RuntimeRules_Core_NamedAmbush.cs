using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NamedAmbush
{
    internal static CardKind AmbushCardName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;

    internal static CardConversionSource AmbushSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static CardLocation AmbushLocation(CardConversionSource source) => new(CardZoneKind.PrivatePersistentPile, source.OwnerSeat,
        $"named-ambush:{source.SkillId}:{source.SkillInstanceId}");
}
