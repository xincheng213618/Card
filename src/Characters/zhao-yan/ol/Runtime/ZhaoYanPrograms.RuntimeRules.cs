namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhao_yan_ol_ZhaoYanPrograms
{
    internal static bool IsTongxieMember(ProgramTongxieArmedEvent arm, int seat) =>
        arm.MemberSeats.Contains(seat);

    internal static int[] ParseTongxieArmSeats(string? seats) =>
        string.IsNullOrEmpty(seats) ? [] : seats.Split(',').Select(seat =>
            int.Parse(seat, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
