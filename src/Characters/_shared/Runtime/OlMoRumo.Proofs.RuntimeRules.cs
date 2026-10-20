namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules__shared_OlMoRumo_Proofs
{
    internal static ProgramTriggerCandidate OlMoRumoCandidate(ProgramSkillFrame frame) =>
        new(frame.OwnerSeat, frame.SkillId, frame.TriggerId ?? "", frame.SkillInstanceId, frame.GameplayHash, 0,
            frame.WindowContext?.OccurrenceIndex ?? 0);
}
