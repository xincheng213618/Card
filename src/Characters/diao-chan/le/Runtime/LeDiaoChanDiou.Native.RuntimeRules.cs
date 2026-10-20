namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_diao_chan_le_LeDiaoChanDiou_Native
{
    internal static bool SameDiouPlan(DiouUsePlan a, DiouUsePlan b) => a.Kind == b.Kind && a.ActionKind == b.ActionKind &&
        a.TargetCardId == b.TargetCardId && a.RequiredKind == b.RequiredKind && a.Adjusted == b.Adjusted && a.Targets.SequenceEqual(b.Targets);
}
