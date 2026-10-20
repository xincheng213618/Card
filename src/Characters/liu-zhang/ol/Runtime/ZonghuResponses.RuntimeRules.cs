namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_zhang_ol_ZonghuResponses
{
    internal static ZonghuPaidOrigin? ZonghuResponseOrigin(ResolutionFrame f)=>f switch{CardUseFrame u=>u.ZonghuResponseOrigin,ProgramSkillFrame p=>p.ZonghuResponseOrigin,_=>null};
}
