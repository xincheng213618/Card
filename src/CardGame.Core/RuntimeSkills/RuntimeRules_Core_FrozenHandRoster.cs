using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_FrozenHandRoster
{
    internal static Dictionary<string,string> FrozenHandParameters(ProgramSkillFrame f,string branch)=>new()
    { ["program-action"]="frozen-hand-roster",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]=branch,
      ["cursor"]=f.FrozenHandRoster!.Cursor.ToString(CultureInfo.InvariantCulture) };
}
