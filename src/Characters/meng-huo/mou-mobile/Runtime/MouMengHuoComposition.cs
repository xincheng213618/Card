namespace CardGame.Core;
internal static class MouMengHuoComposition
{
    internal const string Huoshou = "mou-mobile:huoshou", Zaiqi = "mou-mobile:zaiqi";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouMengHuoAttribution or SkillProgramEffectOp.ObtainMouMengHuoBarbarian or SkillProgramEffectOp.GainMouMengHuoCharge or SkillProgramEffectOp.ResolveMouMengHuoZaiqi;
    private static bool Trigger(SkillProgramTrigger t, string id, SkillProgramTriggerWindow window, SkillProgramEffectOp op) =>
        t.Id == id && t.Window == window && !t.Optional && t.UsageLimit is null && t.UsageScope is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.Effects is [var e] && e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.ActorReference is null && e.TargetReference is null && e.Condition.Kind == SkillProgramConditionKind.Always;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id is not (Huoshou or Zaiqi) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var common = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 && p.DamageModifiers.Count == 0;
        var valid = p.Id switch
        {
            Huoshou => p.CardPolicies is [var policy] && policy.Kind == SkillProgramCardPolicyKind.ExcludeGlobalTarget && policy.CardKinds.SequenceEqual([CardKind.BarbarianAssault]) &&
                p.Triggers is [var a, var b] && Trigger(a, "attribute-after-designation", SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramEffectOp.ResolveMouMengHuoAttribution) &&
                a.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && a.CardKinds.SequenceEqual([CardKind.BarbarianAssault]) &&
                Trigger(b, "obtain-at-play-start", SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramEffectOp.ObtainMouMengHuoBarbarian) && b.Subject == SkillProgramTriggerSubject.Owner,
            Zaiqi => p.CardPolicies.Count == 0 && p.Triggers is [var a, var b] &&
                Trigger(a, "charge-after-damage", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.GainMouMengHuoCharge) && a.Subject == SkillProgramTriggerSubject.DamageSource && a.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                Trigger(b, "spend-at-discard-end", SkillProgramTriggerWindow.DiscardPhaseEnded, SkillProgramEffectOp.ResolveMouMengHuoZaiqi) && b.Subject == SkillProgramTriggerSubject.Owner && b.AllowOwnDiscardPhaseEnded && b.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
            _ => false
        };
        if (!common || !valid) throw new InvalidOperationException(path + ": Mou Meng Huo requires the complete native attribution, Play obtain, 0/7 charge and paid discard-end recipient contracts.");
    }
}
