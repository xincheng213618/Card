namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramCategoryChallenges
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 130)]
    internal static void AssertFrozenSelectedCardSuits(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        foreach (var binding in frame.CardSetBindings.Where(item => item.FrozenSelectedSuits is not null))
        {
            var producers = plan.Instructions.Take(frame.InstructionIndex).Where(effect =>
                effect.ResultBind == binding.Name && effect is
                { Op: SkillProgramEffectOp.SelectOwnedCards, RequireExactCount: true, FreezeSelectedCardSuits: true }).ToArray();
            if (producers.Length != 1 || binding.FrozenSelectedSuits!.Count != binding.CardIds.Count ||
                binding.FrozenSelectedSuits.Any(suit => !Enum.IsDefined(suit)))
                throw new InvalidOperationException("Frozen selected suits lost their exact producer or aligned physical receipt.");
        }
        foreach (var producer in plan.Instructions.Take(frame.InstructionIndex).Where(effect =>
                     effect.FreezeSelectedCardSuits == true))
        {
            var binding = frame.CardSetBindings.SingleOrDefault(item => item.Name == producer.ResultBind);
            if (binding is not null && binding.FrozenSelectedSuits is null)
                throw new InvalidOperationException("The exact selected-card binding lost its frozen suits.");
        }
    }
}
