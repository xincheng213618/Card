namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_da_qiao_xiao_qiao_ol_DaQiaoXiaoQiaoLuoyan
{
    internal static string DaQiaoXiaoQiaoDerivedId(int seat,DaQiaoXiaoQiaoLuoyanSource r)=>$"luoyan:{seat}:{r.CapabilityGrantId.Length}:{r.CapabilityGrantId}:{r.CapabilitySkillInstanceId.Length}:{r.CapabilitySkillInstanceId}:{r.LeafSkillId}";
}
