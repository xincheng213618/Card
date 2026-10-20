using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ya_dan_ol_YaDan
{
    internal static Dictionary<string,string> YaDanArgs(string branch)=>new(){["program-action"]="ya-dan-qingya",["branch"]=branch};
}
