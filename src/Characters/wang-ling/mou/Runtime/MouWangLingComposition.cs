namespace CardGame.Core;

internal static class MouWangLingComposition
{
    internal const string Jichou = "mou:jichou", Mouli = "mou:mouli", Zifu = "mou:zifu";
    [ProgramCatalogValidation(1670)]
    internal static void Validate(string path, SkillProgram p)
    {
        static bool Reserved(SkillProgramEffect e) => e.Op is SkillProgramEffectOp.ResolveMouWangLingJichou or SkillProgramEffectOp.PrepareMouWangLingMouli or SkillProgramEffectOp.ResolveMouWangLingZifu;
        var ours=p.Id is Jichou or Mouli or Zifu;
        if(!ours&&!p.Activations.SelectMany(a=>a.Effects).Concat(p.Triggers.SelectMany(t=>t.Effects)).Any(Reserved))return;
        if(!ours||p.Triggers is not[var t]||p.Activations.Count!=0||p.ViewAs.Count!=0||p.CardPolicies.Count!=0||p.Modifiers.Count!=0||p.DamageModifiers.Count!=0||p.HpSkillGrants.Count!=0||p.BooleanStates.Count!=0||
            t.Subject!=SkillProgramTriggerSubject.Owner||t.Condition.Kind!=SkillProgramTriggerConditionKind.Always||t.Effects.Any(e=>e.Target!=SkillProgramEffectTarget.Owner||e.Condition.Kind!=SkillProgramConditionKind.Always))throw new InvalidOperationException(path+": Wang Ling requires its complete native lifecycle bindings.");
        var valid=p.Id switch {
            Jichou=>t.Id=="unique-turn-gifts"&&t.Window==SkillProgramTriggerWindow.TurnEnding&&t.Optional&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouWangLingJichou}],
            Zifu=>t.Id=="draw-and-retain-names"&&t.Window==SkillProgramTriggerWindow.PlayPhaseStarting&&!t.Optional&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMouWangLingZifu}],
            _=>t.Id=="five-names-awakening"&&t.Window==SkillProgramTriggerWindow.AfterTurnEnded&&!t.Optional&&t.TurnOwnerScope==SkillProgramTurnOwnerScope.Own&&t.UsageScope==SkillUsageScope.Game&&t.UsageLimit==1&&
                t.Effects is[{Op:SkillProgramEffectOp.PrepareMouWangLingMouli},{Op:SkillProgramEffectOp.ChangeMaximumHp,Amount:1},{Op:SkillProgramEffectOp.Recover,Amount:1},{Op:SkillProgramEffectOp.GrantSkills}grant]&&grant.SkillIds.SequenceEqual([Zifu]) };
        if(!valid)throw new InvalidOperationException(path+": Wang Ling changed a reserved source, awakening reward or actual-stage binding.");
    }
}
