namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SuitPreventionBenefitObservers
{
    internal static bool SuitPlacementBeforeDamageEdge(BeforeDamageProgramWindowFrame before, ProgramSkillFrame root) =>
        root.AttackAttempt is { } attack && (before.ContinuationAttackResolutionId ?? before.ParentFrameId) == root.Id &&
        before.Continuation == BeforeDamageProgramContinuation.Attack && before.SourceSeat == attack.SourceSeat &&
        before.TargetSeat == attack.TargetSeat && before.Amount == attack.DamageAmount && before.Nature == attack.Nature && root.AttackReturn is not null;
}
