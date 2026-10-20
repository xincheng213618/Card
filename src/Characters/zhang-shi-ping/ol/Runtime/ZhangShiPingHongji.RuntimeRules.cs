namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhang_shi_ping_ol_ZhangShiPingHongji
{
    internal static CardConversionSource ZhangShiPingSource(ProgramSkillFrame f) => new(f.SkillId, "lifecycle", f.OwnerSeat, f.SkillInstanceId);
}
