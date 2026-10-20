using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_pang_ji_ol_Douyu
{
    internal static CardConversionSource DouyuSource(ProgramTriggerCandidate c) => new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId);

    internal static CardConversionSource DouyuSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
}
