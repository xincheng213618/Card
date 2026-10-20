namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_xiang_chong_ol_XiangChongProofs
{
    internal static IEnumerable<int> XiangSilverLionSeats(XiangChongReceipt r)=>r.Payments.Concat(r.Replaced is{}p?[p]:Array.Empty<XiangChongCard>()).Where(c=>c.Kind==CardKind.SilverLion&&c.From.Zone==CardZoneKind.Equipment&&c.From.OwnerSeat is not null).Select(c=>c.From.OwnerSeat!.Value);
}
