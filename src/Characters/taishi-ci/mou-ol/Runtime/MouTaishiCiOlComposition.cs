namespace CardGame.Core;

internal static class MouTaishiCiOlComposition
{
    internal const string Dulie = "mou-ol:dulie", Douchan = "mou-ol:douchan";

    [ProgramCatalogValidation(1480)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id is not (Dulie or Douchan)) return;
        if (program.Triggers is not [var trigger] || program.Activations.Count != 0 ||
            program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 ||
            program.BooleanStates.Count != 0 || trigger.Effects is not [var effect] ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            effect.Target != SkillProgramEffectTarget.Owner || effect.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": OL Taishi Ci requires its complete two native trigger programs.");
        if (program.Id == Dulie)
        {
            if (trigger.Id != "sole-incoming-extra-effect-and-range-draw" ||
                effect.Op != SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange || effect.Amount != 5)
                throw new InvalidOperationException(path + ": OL Dulie requires its native sole-target repeat and range-five draw cap.");
            RepeatIncomingSoleCardEffectAndDrawByRangeDescriptor.ValidateTrigger(path, trigger);
        }
        else
        {
            if (trigger.Id != "preparation-duel-or-growth" ||
                effect.Op != SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall ||
                effect.Amount != 1 || effect.NumberExpression is not null || effect.CapGrowthByInitialPlayerCount != true)
                throw new InvalidOperationException(path + ": OL Douchan obtains one real Duel and caps permanent shortfall growth by initial player count.");
            AcquireDeckKindSequentiallyAndGrowOnShortfallDescriptor.ValidateTrigger(path, trigger);
        }
    }
}
