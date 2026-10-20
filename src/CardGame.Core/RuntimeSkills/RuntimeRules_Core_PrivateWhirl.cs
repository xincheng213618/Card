using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PrivateWhirl
{
    internal static CardConversionSource WhirlSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);

    internal static CardLocation WhirlLocation(CardConversionSource source) => new(CardZoneKind.PrivatePersistentPile, source.OwnerSeat, $"private-whirl:{source.SkillId}:{source.SkillInstanceId}");
}
