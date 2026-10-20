namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_KuangfuReceipts
{
    internal static bool IsKuangfuPending(ProgramSkillFrame f) => f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == f.OwnerSeat;
}
