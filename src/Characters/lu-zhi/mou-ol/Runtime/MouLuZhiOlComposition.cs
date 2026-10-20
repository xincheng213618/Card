namespace CardGame.Core;
internal static class MouLuZhiOlComposition
{
    internal const string Sibing = "mou-ol:sibing", Liance = "mou-ol:liance";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouLuZhiSibingPress or SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation or SkillProgramEffectOp.ResolveMouLuZhiLiance;
    [ProgramCatalogValidation(1510)]
    internal static void Validate(string path, SkillProgram p)
    {
        var effects = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects));
        if (p.Id is not (Sibing or Liance) && !effects.Any(e => Handles(e.Op))) return;
        var valid = p.Id is Sibing or Liance && p.Triggers.Count == 2 && p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0;
        foreach (var t in p.Triggers)
        {
            valid &= t.Optional == (p.Id == Liance) && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, ActorReference: null, Condition.Kind: SkillProgramConditionKind.Always }];
            valid &= p.Id == Sibing ? t.UsageScope is null && t.UsageLimit is null && t.Subject is null &&
                (t.Id == "red-press" && t.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !t.SingleActionInstance && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouLuZhiSibingPress ||
                 t.Id == "undamaged-retaliation" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Target && t.SingleActionInstance && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouLuZhiSibingRetaliation) :
                p.Id == Liance && t.Subject == SkillProgramTriggerSubject.Owner && t.OwnerRelation is null && t.UsageScope == SkillUsageScope.Round && t.UsageLimit == 1 && t.NamedUsageGroup == "draw-to-max" && t.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch &&
                (t.Id == "hand-lost" && t.Window == SkillProgramTriggerWindow.CardsMoved && t.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
                 t.Id == "hand-gained" && t.Window == SkillProgramTriggerWindow.CardsGained && t.DestinationZones.SequenceEqual([CardZoneKind.Hand])) && t.Effects[0].Op == SkillProgramEffectOp.ResolveMouLuZhiLiance;
        }
        if (!valid) throw new InvalidOperationException(path + ": OL Mou Lu Zhi requires complete independent Sibing clauses and one shared actual-round Liance budget.");
    }
}
