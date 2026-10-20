namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_cai_yong_le_LeCaiYongJiaowei
{
    internal static CardConversionSource JiaoweiSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    internal static bool IsJiaoweiOwnedZone(CardLocation location, int owner) =>
        location.OwnerSeat == owner && location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment;
}
