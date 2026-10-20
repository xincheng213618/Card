using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_guo_tu_ol_WeijieNeeds
{
    internal const string WeijieOpen = "weijie-request";

    internal static CardConversionSource WeijieSource(ProgramSkillFrame f)=>new(f.SkillId,f.ActivationId,f.OwnerSeat,f.SkillInstanceId);

    internal static bool WeijieIsDying(WeijieNeed n)=>n.Intent==WeijieIntent.Dying;

    internal static bool WeijieIsPlayed(WeijieNeed n)=>n.Intent is WeijieIntent.Duel or WeijieIntent.Group or WeijieIntent.FactionDodge or WeijieIntent.FactionSlashPlayed;

    internal static PendingDecision WeijieNativeDecision(PendingDecision p)=>p.Choices.Any(c=>c.Parameters.ContainsKey(WeijieOpen))?p with{Choices=Array.AsReadOnly(p.Choices.Where(c=>!c.Parameters.ContainsKey(WeijieOpen)).ToArray())}:p;
}
