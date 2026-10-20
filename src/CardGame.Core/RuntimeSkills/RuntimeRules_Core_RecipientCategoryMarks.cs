using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RecipientCategoryMarks
{
    internal static RecipientCategoryMarkKind RecipientMarkKind(SkillProgramEffectOp op) => op switch {
        SkillProgramEffectOp.SpendCategoryMarkForDyingRecovery => RecipientCategoryMarkKind.Rescue,
        SkillProgramEffectOp.SpendCategoryMarkForAreaDiscard => RecipientCategoryMarkKind.Discard,
        SkillProgramEffectOp.SpendCategoryMarkForSlashTargets => RecipientCategoryMarkKind.ExtraTargets,
        _ => throw new InvalidOperationException("A gift has no predetermined category mark.") };

    internal static PlayerMarkerKind RecipientPublicMarker(RecipientCategoryMarkKind kind) => kind switch {
        RecipientCategoryMarkKind.Rescue => PlayerMarkerKind.CategoryLei,
        RecipientCategoryMarkKind.Discard => PlayerMarkerKind.CategoryFu,
        _ => PlayerMarkerKind.CategorySong };

    internal static CardConversionSource RecipientMarkSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static string RecipientMarkReason(ProgramSkillFrame f, string suffix) => $"skill-program.{f.SkillId}.recipient-category.{suffix}";

    internal static Dictionary<string, string> RecipientMarkParameters(ProgramSkillFrame f, string action) => new()
    { ["program-action"] = "recipient-category-mark", ["mark-action"] = action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
}
