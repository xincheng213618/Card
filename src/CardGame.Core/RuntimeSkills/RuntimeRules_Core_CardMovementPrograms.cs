namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardMovementPrograms
{
    internal static bool IsGainPhaseQualified(SkillProgramTrigger trigger, int ownerSeat, CardMovementTiming? timing) =>
        trigger.GainPhaseQualification is null || timing is not null &&
        (timing.Phase != TurnPhase.Draw || timing.PhaseActorSeat != ownerSeat);
}
