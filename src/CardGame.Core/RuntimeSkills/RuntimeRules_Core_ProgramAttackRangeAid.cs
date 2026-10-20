namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramAttackRangeAid
{
    internal static Dictionary<string, string> AttackRangeAidParameters(
        ProgramSkillFrame frame,
        ProgramAttackRangeAid aid,
        CardMoveReason reason,
        string action) =>
        new()
        {
            ["program-action"] = action,
            ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["responder-index"] = aid.ResponderIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["target-seat"] = aid.TargetSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["move-reason"] = reason.Value
        };
}
