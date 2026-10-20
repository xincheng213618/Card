namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RoundOnceCardlessNativeResponses
{
    internal static RoundOnceCardlessBasicUseReceipt? RoundOnceCardlessResponseReceipt(ResolutionFrame owner)=>owner switch{CardUseFrame use=>use.RoundOnceCardlessBasicResponse,ProgramSkillFrame program=>program.RoundOnceCardlessBasicResponse,_=>null};
}
