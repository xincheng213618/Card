namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_AlcoholIdentityPolicies
{
    internal static bool HasDyingAlcoholProducer(SkillProgramTrigger trigger) =>
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseBoundCardAsDyingAlcohol) ||
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseVirtualDyingAlcohol) ||
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol);
}
