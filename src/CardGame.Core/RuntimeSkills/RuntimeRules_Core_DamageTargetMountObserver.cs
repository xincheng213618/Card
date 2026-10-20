namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageTargetMountObserver
{
    internal static bool MountObserverCandidateMatches(ProgramSkillFrame p,ProgramTriggerCandidate c)=>
        p.OwnerSeat==c.OwnerSeat && p.SkillId==c.SkillId && p.TriggerId==c.BindingId && p.SkillInstanceId==c.SkillInstanceId &&
        p.GameplayHash==c.GameplayHash && p.WindowContext?.OccurrenceIndex==c.OccurrenceIndex;
}
