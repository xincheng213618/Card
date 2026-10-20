namespace CardGame.Core;

internal static class MouLuXunComposition
{
    internal const string Qianxun = "mou-mobile:qianxun", Lianying = "mou-mobile:lianying";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.RecordMouLuXunQianxun or
        SkillProgramEffectOp.UseMouLuXunRecordedTrick or SkillProgramEffectOp.DistributeMouLuXunLianying;
    [ProgramCatalogValidation(1500)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var effects = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects));
        if (p.Id is not (Qianxun or Lianying))
        {
            if (effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Lu Xun's operations require their original complete programs.");
            return;
        }
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.BooleanStates.Count != 0 || p.Triggers.Count != (p.Id == Qianxun ? 2 : 1))
            throw new InvalidOperationException(path + ": mobile Lu Xun requires all recorded-effect, recorded-use and turn-loss branches.");
        foreach (var t in p.Triggers)
        {
            if (t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageScope is not null || t.UsageLimit is not null ||
                t.NamedUsageGroup is not null || t.MarkerCost is not null || t.SingleActionInstance || t.IncludeResponseUses ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
                t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
                !(p.Id == Qianxun && t.Id == "record-and-hold" && !t.Optional && t.Window == SkillProgramTriggerWindow.CardEffectBeforeApply &&
                    t.OwnerRelation == SkillProgramCardActionOwnerRelation.Target && e.Op == SkillProgramEffectOp.RecordMouLuXunQianxun ||
                  p.Id == Qianxun && t.Id == "use-recorded-trick" && t.Optional && t.Window == SkillProgramTriggerWindow.PlayPhaseStarting &&
                    t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && e.Op == SkillProgramEffectOp.UseMouLuXunRecordedTrick ||
                  p.Id == Lianying && t.Id == "other-turn-loss-distribution" && t.Optional && t.Window == SkillProgramTriggerWindow.AfterTurnEnded &&
                    t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving && e.Op == SkillProgramEffectOp.DistributeMouLuXunLianying))
                throw new InvalidOperationException(path + ": mobile Lu Xun's complete native timing and bounded choices cannot be transplanted or narrowed.");
        }
    }
}
