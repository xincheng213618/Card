namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_guo_tu_ol_WeijieResponses
{
    internal static WeijiePaidOrigin? WeijieResponseOrigin(ResolutionFrame f)=>f switch{CardUseFrame u=>u.WeijieResponseOrigin,ProgramSkillFrame p=>p.WeijieResponseOrigin,_=>null};
}
