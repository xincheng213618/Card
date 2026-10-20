using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_zhang_ol_ZonghuNeeds
{
    internal const string ZonghuOpen = "zonghu-request";

    internal static CardConversionSource ZonghuSource(ProgramSkillFrame f)=>new(f.SkillId,f.ActivationId,f.OwnerSeat,f.SkillInstanceId);

    internal static bool ZonghuIsDying(ZonghuNeed n)=>n.Intent==ZonghuIntent.Dying;

    internal static bool ZonghuIsPlayed(ZonghuNeed n)=>n.Intent is ZonghuIntent.Duel or ZonghuIntent.Group or ZonghuIntent.FactionDodge or ZonghuIntent.FactionSlashPlayed;

    internal static PendingDecision ZonghuNativeDecision(PendingDecision p)
    { p=CardGame.Core.RuntimeSkills.RuntimeRules_huan_shu_jun_ol_HuanpeiRequests.HuanpeiNativeDecision(p);return p.Choices.Any(c=>c.Parameters.ContainsKey(ZonghuOpen))?p with{Choices=Array.AsReadOnly(p.Choices.Where(c=>!c.Parameters.ContainsKey(ZonghuOpen)).ToArray())}:p; }
}
