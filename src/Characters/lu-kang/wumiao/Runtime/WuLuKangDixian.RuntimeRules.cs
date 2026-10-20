using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_kang_wumiao_WuLuKangDixian
{
    internal static CardConversionSource WuLuKangDixianSource(ProgramSkillFrame f) => new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId);

    internal static IReadOnlyList<PromptChoice> WuLuKangDixianChoices(ProgramSkillFrame f) => Array.AsReadOnly(Enumerable.Range(1, 13).Select(rank =>
        new PromptChoice(new($"wu-lu-kang.dixian.{f.Id}.{rank}"), rank switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => rank.ToString(CultureInfo.InvariantCulture) }, [], [],
            new Dictionary<string, string> { ["program-action"] = "wu-lu-kang-dixian", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["rank"] = rank.ToString(CultureInfo.InvariantCulture) })).ToArray());
}
