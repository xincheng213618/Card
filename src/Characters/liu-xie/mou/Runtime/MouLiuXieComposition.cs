namespace CardGame.Core;

internal static class MouLiuXieComposition
{
    internal const string Zhanban = "mou:zhanban", Chensheng = "mou:chensheng", Tiancheng = "mou:tiancheng";
    [ProgramCatalogValidation(1700)]
    internal static void Validate(string path, SkillProgram program)
    {
        static bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouLiuXieZhanban or SkillProgramEffectOp.PrepareMouLiuXieChensheng;
        var ours = program.Id is Zhanban or Chensheng;
        if (!ours && !program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects)).Any(Reserved)) return;
        if (!ours || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.DamageModifiers.Count != 0 || program.HpSkillGrants.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Liu Xie requires his complete native adjustment or actual-turn-end binding.");
        var valid = program.Id == Zhanban
            ? program.Triggers.Count == 0 && program.Activations is [var a] && a.Id == "adjust-all-hands" &&
                a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 0 && a.MaxTargets == 0 && a.UsesPerTurn is null && a.UsesPerPhase == 1 &&
                a.Effects is [{ Op: SkillProgramEffectOp.ResolveMouLiuXieZhanban, Target: SkillProgramEffectTarget.Owner }]
            : program.Activations.Count == 0 && program.Triggers is [var t] && t.Id == "other-actual-turn-end" &&
                t.Window == SkillProgramTriggerWindow.AfterTurnEnded && t.Subject == SkillProgramTriggerSubject.Owner && !t.Optional &&
                t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
                t.Effects is [{ Op: SkillProgramEffectOp.PrepareMouLiuXieChensheng, Target: SkillProgramEffectTarget.Owner }, { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1 }];
        if (!valid) throw new InvalidOperationException(path + ": Liu Xie changed the reserved adjustment or compulsory Chensheng reward.");
    }
}
