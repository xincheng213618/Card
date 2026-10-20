namespace CardGame.Core;

internal static class MoDiaoChanComposition
{
    internal const string Huanhuo = "ol:mo-huanhuo", Qingshi = "ol:mo-qingshi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound or SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse or
        SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep or SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect or SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage or SkillProgramEffectOp.ResolveMoDiaoChanQingshiDiscard;
    internal static bool HuanhuoCompleted(SkillProgramTrigger t) => t.Id == "affected-use-completed" && t.Window == SkillProgramTriggerWindow.CardUseCompleted &&
        t.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && t.IncludeResponseUses && t.SingleActionInstance && !t.Optional && t.Effects is
        [{ Op: SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoUse, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
    [ProgramCatalogValidation(1360)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var owned = p.Id is Huanhuo or Qingshi;
        if (!owned) { if (p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Magic Diao Chan operations cannot migrate to an unrelated source."); return; }
        bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op) => t.Id == id && t.Window == window && !t.Optional &&
            t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && t.UsageLimit is null;
        var valid = p.Id == Huanhuo ? p.Triggers is [var round, var completed] &&
            Trigger(round, "round-draw-and-select", SkillProgramTriggerWindow.RoundStarting, SkillProgramEffectOp.ResolveMoDiaoChanHuanhuoRound) && HuanhuoCompleted(completed) :
            p.Triggers is [var prep, var redirect, var damage, var discard] &&
            Trigger(prep, "prepare-enter-or-refresh", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveMoDiaoChanQingshiPrep) && prep.Subject == SkillProgramTriggerSubject.Owner &&
            Trigger(redirect, "cohort-unique-target", SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ResolveMoDiaoChanQingshiRedirect) && redirect.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer &&
            Trigger(damage, "cohort-damage-draw", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage) && damage.Subject == SkillProgramTriggerSubject.Any && damage.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
            Trigger(discard, "cohort-nonuse-recapture", SkillProgramTriggerWindow.DiscardPileReceived, SkillProgramEffectOp.ResolveMoDiaoChanQingshiDiscard) && discard.Subject == SkillProgramTriggerSubject.Owner && discard.MovementOccurrence is null && discard.SourceZones.Count == 0;
        if (!valid || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Magic Diao Chan requires complete original Huanhuo and Qingshi trigger programs.");
    }
}
internal sealed class MoDiaoChanCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => MoDiaoChanComposition.Huanhuo;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    { if (!MoDiaoChanComposition.HuanhuoCompleted(trigger)) throw new InvalidOperationException("Huanhuo requires its exact completed native true Use, excluding pure Played."); }
}
