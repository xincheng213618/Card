using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ActualHandGainPrograms
{
    internal static CardConversionSource ActualHandGainSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);

    internal static SkillProgramCardCategory ActualHandGainCategory(CardKind kind) =>
        new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Single(c => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesSkillProgramCardCategory(kind, c));

    internal static Dictionary<string, string> ActualHandGainParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "actual-hand-gain-category", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
}
