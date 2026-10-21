namespace CardGame.Core;
internal static class MouLuSuAnniversaryComposition
{
    [ProgramCatalogValidation(1830)] internal static void Validate(string path,SkillProgram p)
    {
        var own = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.InitializeMouLuSuMengmou or SkillProgramEffectOp.ScheduleMouLuSuMingshi or SkillProgramEffectOp.ResolveMouLuSuMingshi or SkillProgramEffectOp.ResolveMouLuSuMengmou or SkillProgramEffectOp.RecoverMouLuSuMengmouDamage);
        if (!own && p.Id is not (MouLuSuAnniversaryRuntime.Mingshi or MouLuSuAnniversaryRuntime.Mengmou)) return;
        bool T(SkillProgramTrigger t,string id,SkillProgramTriggerWindow w,SkillProgramEffectOp op,bool optional) => t.Id == id && t.Window == w && t.Optional == optional && t.Effects is [{ Op: var effect, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] && effect == op;
        var mingshi = p.Id == MouLuSuAnniversaryRuntime.Mingshi && p.BooleanStates.Count == 0 && p.Triggers.Count == 2 && p.Triggers[0] is { Id: "extra-normal-draw", Window: SkillProgramTriggerWindow.DrawPhaseStarting, Optional: true, Effects: [{ Op: SkillProgramEffectOp.AdjustNormalDraw, Amount: 2, Target: SkillProgramEffectTarget.Owner }, { Op: SkillProgramEffectOp.ScheduleMouLuSuMingshi, Target: SkillProgramEffectTarget.Owner }] } && T(p.Triggers[1],"show-gift-refill",SkillProgramTriggerWindow.AfterNormalDraw,SkillProgramEffectOp.ResolveMouLuSuMingshi,false);
        var mengmou = p.Id == MouLuSuAnniversaryRuntime.Mengmou && p.BooleanStates is [{ Id: "yin", InitialValue: false }] && p.Triggers.Count == 3 && T(p.Triggers[0],"opening-state",SkillProgramTriggerWindow.GameStarting,SkillProgramEffectOp.InitializeMouLuSuMengmou,false) && T(p.Triggers[1],"actual-owned-acquisition",SkillProgramTriggerWindow.CardsGained,SkillProgramEffectOp.ResolveMouLuSuMengmou,true) && p.Triggers[1].MovementOccurrence == SkillProgramMovementOccurrence.PerBatch && T(p.Triggers[2],"requested-slash-damage",SkillProgramTriggerWindow.AfterDamageApplied,SkillProgramEffectOp.RecoverMouLuSuMengmouDamage,false);
        if ((!mingshi && !mengmou) || p.Activations.Count != 0 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.DamageModifiers.Count != 0) throw new InvalidOperationException(path + ": Mou Lu Su requires his immutable current additive Mingshi and actual owned-acquisition Use/Played Mengmou definitions.");
    }
}
