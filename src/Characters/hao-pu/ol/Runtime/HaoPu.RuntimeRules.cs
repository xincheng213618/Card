using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_hao_pu_ol_HaoPu
{
    internal static CardConversionSource ZhenyingSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static ZhenyingHandPlan ZhenyingPlan(ZhenyingReceipt r) => r.SelectionIndex == 0 ? r.OwnerPlan : r.OtherPlan;

    internal static ZhenyingReceipt WithZhenyingPlan(ZhenyingReceipt r, ZhenyingHandPlan plan) =>
        r.SelectionIndex == 0 ? r with { OwnerPlan = plan } : r with { OtherPlan = plan };

    internal static int ZhenyingKeptCount(ZhenyingHandPlan p) => Math.Min(p.OriginalIds.Count, Math.Max(p.DesiredCount ?? 0, p.ProtectedIds?.Count ?? 0));
}
