namespace CardGame.Core;
internal static class MoCaoCaoComposition
{
    internal const string Fuzai = "ol:mo-fuzai", Bachao = "ol:mo-bachao", Baibi = "equipment:baibi-dao", BaibiBinding = "after-slash-damage";
    [ProgramCatalogValidation(1600)]
    internal static void Validate(string path, SkillProgram p)
    {
        var own = p.Id is Fuzai or Bachao or Baibi;
        if (!own && !p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.InitializeMoCaoCaoFuzai or SkillProgramEffectOp.ResolveMoCaoCaoBachao or SkillProgramEffectOp.ResolveMoCaoCaoBaibi)) return;
        var valid = own && p.Activations.Count == 0 && p.Modifiers.Count == 0 && p.BooleanStates.Count == 0 && p.CardPolicies.Count == 0 && p.Triggers.Count == 1;
        if (!valid) throw new InvalidOperationException(path + ": magic Cao Cao requires complete Fuzai/Bachao and the native Baipi weapon producer.");
        var t = p.Triggers[0];
        valid &= t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null &&
            t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
            !t.SingleActionInstance && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Priority == 0 &&
            t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
        if (p.Id == Fuzai)
        {
            valid &= t is { Id: "initialize", Window: SkillProgramTriggerWindow.GameStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: false } &&
                t.Effects[0].Op == SkillProgramEffectOp.InitializeMoCaoCaoFuzai && p.ViewAs.Count == 2;
            foreach (var v in p.ViewAs)
                valid &= v.InputKinds.Count == 0 && v.InputCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) && v.InputSuits.Count == 0 &&
                    v.InputCount == 1 && v.SourceZones.SequenceEqual([CardZoneKind.Hand,CardZoneKind.Equipment]) && v.ForPlay && !v.ForResponse && v.UseOnly && v.SingleCardTrickUse &&
                    (v.Id == "equipment-as-draw-two" && v.OutputKind == CardKind.DrawTwo || v.Id == "equipment-as-borrowed-sword" && v.OutputKind == CardKind.BorrowedSword);
        }
        else
        {
            valid &= p.ViewAs.Count == 0;
            valid &= p.Id == Bachao ? t is { Id: "simultaneous-gifts", Window: SkillProgramTriggerWindow.PlayPhaseStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: true } && t.Effects[0].Op == SkillProgramEffectOp.ResolveMoCaoCaoBachao :
                t is { Id: BaibiBinding, Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource, Optional: true, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage } && t.Effects[0].Op == SkillProgramEffectOp.ResolveMoCaoCaoBaibi;
        }
        if (!valid) throw new InvalidOperationException(path + ": magic Cao Cao bindings must preserve the full native equipment conversions, actual Play-start simultaneous gifts and optional genuine Baipi Slash-damage claim.");
    }
}
