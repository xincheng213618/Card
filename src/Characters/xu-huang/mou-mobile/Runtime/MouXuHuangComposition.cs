namespace CardGame.Core;

internal static class MouXuHuangComposition
{
    internal const string Duanliang = "mou-mobile:duanliang", Shipo = "mou-mobile:shipo";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveMouXuHuangDuanliang or SkillProgramEffectOp.ResolveMouXuHuangShipo;
    private static bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner &&
        e.Condition.Kind == SkillProgramConditionKind.Always && e.Amount == 0 && e.ActorReference is null && e.TargetReference is null &&
        e.CardOwnerRef is null && e.ChooserRef is null && e.SourceBind is null && e.ResultBind is null && e.NumberExpression is null && e.Zones.Count == 0 && !e.AllowDecline && !e.AwaitMovementTriggers;
    private static bool Empty(SkillProgram p) => p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 &&
        p.HpSkillGrants.Count == 0 && p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 && p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    internal static bool IsDuanliang(SkillProgram p) => p.Id == Duanliang && Empty(p) && p.Triggers.Count == 0 && p.Activations is [var a] &&
        a.Id == "private-mouyi" && a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 &&
        a.TargetKind == SkillProgramTargetKind.OtherLiving && a.UsesPerPhase == 1 && a.UsesPerTurn is null &&
        a.Effects is [var e] && Plain(e, SkillProgramEffectOp.ResolveMouXuHuangDuanliang);
    internal static bool IsShipo(SkillProgram p) => p.Id == Shipo && Empty(p) && p.Activations.Count == 0 && p.Triggers is [var t] &&
        t.Id == "ending-gift-or-damage" && t.Window == SkillProgramTriggerWindow.TurnEnding && t.Subject == SkillProgramTriggerSubject.Owner &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Optional && t.OwnerRelation is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
        t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.CardKinds.Count == 0 && t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 && t.MovementReasons.Count == 0 &&
        t.SourceSkillId is null && t.SourceViewAsId is null && !t.IncludeResponseUses && !t.SingleActionInstance &&
        t.Effects is [var e] && Plain(e, SkillProgramEffectOp.ResolveMouXuHuangShipo);
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op));
        if ((reserved || p.Id is Duanliang or Shipo) && !IsDuanliang(p) && !IsShipo(p))
            throw new InvalidOperationException(path + ": mobile Xu Huang requires the complete private once-per-actual-Play strategy or own Ending Hand-gift/native-damage and exact obtained-card redistribution contract.");
    }
}
