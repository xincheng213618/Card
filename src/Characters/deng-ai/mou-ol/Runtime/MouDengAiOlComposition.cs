namespace CardGame.Core;
internal static class MouDengAiOlComposition
{
    internal const string Jigu = "mou-ol:jigu", Jiewan = "mou-ol:jiewan";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.CollectMouDengAiJigu or SkillProgramEffectOp.ExchangeMouDengAiJigu or SkillProgramEffectOp.UseMouDengAiJiewan or SkillProgramEffectOp.GrowMouDengAiJiewan;
    [ProgramCatalogValidation(1560)]
    internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id is not (Jigu or Jiewan) && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) return;
        var valid = p.Id is Jigu or Jiewan && p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.CardPolicies.Count == 0 && p.Modifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Triggers.Count == (p.Id == Jigu ? 3 : 4);
        foreach (var t in p.Triggers)
        {
            valid &= !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && !t.SingleActionInstance && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null && t.MarkerCost is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
                t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }];
            var op = t.Effects.FirstOrDefault()?.Op;
            valid &= t.Priority == (t.Id.StartsWith("exchange-", StringComparison.Ordinal) ? 10 : 0);
            if (t.Id == "collect-used") valid &= p.Id == Jigu && op == SkillProgramEffectOp.CollectMouDengAiJigu && t.Window == SkillProgramTriggerWindow.DiscardPileReceived && t.MovementOccurrence is null;
            else
            {
                var own = t.Id.EndsWith("-own", StringComparison.Ordinal);
                valid &= t.TurnOwnerScope == (own ? SkillProgramTurnOwnerScope.Own : SkillProgramTurnOwnerScope.OtherLiving);
                valid &= p.Id == Jigu ? t.Id == (own ? "exchange-own" : "exchange-otherLiving") && op == SkillProgramEffectOp.ExchangeMouDengAiJigu && t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow :
                    t.Id == (own ? "snatch-own" : "snatch-otherLiving") && op == SkillProgramEffectOp.UseMouDengAiJiewan && t.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
                    t.Id == (own ? "grow-own" : "grow-otherLiving") && op == SkillProgramEffectOp.GrowMouDengAiJiewan && t.Window == SkillProgramTriggerWindow.TurnEnding;
            }
        }
        if (!valid) throw new InvalidOperationException(path + ": OL Mou Deng Ai requires its complete non-Heart Use batch, equal-maximum-HP exchange and every actual Preparation/Ending bindings.");
    }
}
