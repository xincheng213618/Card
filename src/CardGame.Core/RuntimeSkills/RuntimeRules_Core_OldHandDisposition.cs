using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OldHandDisposition
{
    internal static Dictionary<string,string> OldHandParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "old-hand-disposition", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
}
