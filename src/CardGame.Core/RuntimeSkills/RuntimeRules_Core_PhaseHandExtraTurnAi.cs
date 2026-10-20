namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PhaseHandExtraTurnAi
{
    // This flag is an ephemeral estimate input, never a pending rule object or
    // stored declaration. The runtime still rechecks its own real payment.
    internal static ProgramAiPublicContext WithPhaseHandExtraTurnTargetPricing(
        ProgramAiPublicContext context, SkillProgram program, SkillProgramTrigger trigger) =>
        ProgramPhaseHandExtraTurnAi.IsLinkedPayment(program, trigger)
            ? context with { PricePhaseHandExtraTurnTarget = true } : context;
}
