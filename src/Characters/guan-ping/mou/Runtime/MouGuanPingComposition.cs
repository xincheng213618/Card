namespace CardGame.Core;
internal static class MouGuanPingComposition
{
    internal const string Skill = "mou:wuwei", Binding = "whole-color-slash", Activation = "color-hand-slash", Trigger = "paid-color-options";
    [ProgramCatalogValidation(1720)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id != Skill && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveMouGuanPingWuwei)) return;
        if (p.Id != Skill || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 ||
            p.ViewAs is not [{ Id: Binding, VariableInputCount: true, OutputKind: CardKind.Slash, ForPlay: true, ForResponse: false, DistanceUnlimited: true, SameSuit: false } v] || !v.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            p.Activations is not [{ Id: Activation, MinCards: 1, MaxCards: int.MaxValue, MinTargets: 1, MaxTargets: 1, UsesPerTurn: null, UsesPerPhase: null } a] ||
            a.Effects is not [{ Op: SkillProgramEffectOp.UseSelectedCardsAs, Target: SkillProgramEffectTarget.SelectedTarget, SourceBind: Binding, OutputKind: CardKind.Slash }] ||
            p.Triggers is not [{ Id: Trigger, Window: SkillProgramTriggerWindow.CardUseCommitted, OwnerRelation: SkillProgramCardActionOwnerRelation.ConversionSource, SourceSkillId: Skill, SourceViewAsId: Binding, Optional: false } t] ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveMouGuanPingWuwei, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Wuwei requires its actual whole-color Hand Slash and original paid-use option cursor.");
    }
}
