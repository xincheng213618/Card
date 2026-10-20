namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_cheng_mou_mobile_MouLiuChengNative
{
    internal static bool SameMouLiuChengPlan(MouLiuChengUsePlan a, MouLiuChengUsePlan b) => a.Kind == b.Kind && a.ActionKind == b.ActionKind &&
        a.TargetCardId == b.TargetCardId && a.RequiredKind == b.RequiredKind && a.Adjusted == b.Adjusted && a.Targets.SequenceEqual(b.Targets);
}
