namespace CardGame.Core;

internal static class MouDongZhaoComposition
{
    internal const string Shunji = "mou-ol:shunji", Yishi = "mou-ol:yishi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouDongZhaoShunji or
        SkillProgramEffectOp.ResolveMouDongZhaoYishi or SkillProgramEffectOp.DrawMouDongZhaoYishiLoss;
    [ProgramCatalogValidation(1570)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var reserved = program.Triggers.SelectMany(t => t.Effects).Concat(program.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op));
        if (program.Id is not (Shunji or Yishi))
        {
            if (reserved) throw new InvalidOperationException(path + ": OL Dong Zhao's operations require their own complete bindings.");
            return;
        }
        var plain = program.ViewAs.Count == 0 && program.Modifiers.Count == 0 && program.CardPolicies.Count == 0 && program.BooleanStates.Count == 0;
        bool Effect(IReadOnlyList<SkillProgramEffect> effects, SkillProgramEffectOp op) =>
            effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        if (!plain || program.Triggers.Count != 1) throw new InvalidOperationException(path + ": OL Dong Zhao requires its complete native bindings.");
        var trigger = program.Triggers[0];
        var valid = program.Id == Shunji
            ? program.Activations.Count == 0 && trigger.Id == "damage-draw-and-gift" && trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
              trigger.Subject == SkillProgramTriggerSubject.Any && trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && trigger.Optional && trigger.UsageScope is null && trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
              Effect(trigger.Effects, SkillProgramEffectOp.ResolveMouDongZhaoShunji)
            : program.Activations is [{ Id: "keep-suits-and-move", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0,
                  TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerTurn: null, UsesPerPhase: 1, Condition.Kind: SkillProgramConditionKind.Always } activation] &&
              Effect(activation.Effects, SkillProgramEffectOp.ResolveMouDongZhaoYishi) && trigger.Id == "follow-suit-loss" &&
              trigger.Window == SkillProgramTriggerWindow.CardsMoved && trigger.Subject == SkillProgramTriggerSubject.Owner && !trigger.Optional &&
              trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch && trigger.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) &&
              trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always && Effect(trigger.Effects, SkillProgramEffectOp.DrawMouDongZhaoYishiLoss);
        if (!valid) throw new InvalidOperationException(path + ": OL Dong Zhao requires its native round-victim gift and exact suit-loss obligation.");
    }
}
