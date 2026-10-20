namespace CardGame.Core;

internal static class MouYuanShaoComposition
{
    internal const string Luanji = "mou-mobile:luanji", Xueyi = "mou-mobile:xueyi", Pair = "any-hand-pair-as-arrow-barrage";
    internal static bool TargetTrigger(string skill, SkillProgramTrigger t) => skill == Xueyi && t.Id == "qun-target-reward";
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op == SkillProgramEffectOp.ReserveMouYuanShaoReward);
        var pair = p.ViewAs.Any(v => v.Id == Pair);
        if (p.Id is not (Luanji or Xueyi))
        { if (reserved || pair) throw new InvalidOperationException(path + ": mobile Yuan Shao reserved bindings cannot be transplanted."); return; }
        if (p.Triggers is not [var t] || t.Optional || t.UsageLimit is not null || t.UsageScope is not null ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ReserveMouYuanShaoReward, Target: SkillProgramEffectTarget.Owner },
                { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 1 }] ||
            t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always) || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.SingleActionInstance || t.IncludeResponseUses ||
            p.Modifiers.Count != 0 || p.CardIdentities.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": mobile Yuan Shao requires its native one-card reward and actual-gain cap.");
        if (p.Id == Luanji)
        {
            if (t.Id != "arrow-dodge-reward" || t.Window != SkillProgramTriggerWindow.CardResponseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer ||
                !t.CardKinds.SequenceEqual([CardKind.Dodge]) || p.CardPolicies.Count != 0 || p.ViewAs is not [var v] || v.Id != Pair ||
                v.InputCount != 2 || v.SameSuit || v.OutputKind != CardKind.ArrowBarrage || !v.ForPlay || v.ForResponse ||
                v.InputKinds.Count != 0 || v.InputSuits.Count != 0 || !v.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
                v.DistanceUnlimited || v.DamageBonus != 0 || v.UsesPerPhase is not null || v.Condition.Kind != SkillProgramConditionKind.Always ||
                p.Activations is not [var a] || a.Id != Pair || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null ||
                a.MinCards != 2 || a.MaxCards != 2 || a.MinTargets != 0 || a.MaxTargets != 0 || a.SelectedCardsSameSuit ||
                !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.Effects is not [{ Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.ArrowBarrage, SourceBind: Pair }])
                throw new InvalidOperationException(path + ": Luanji requires any two native Hand materials once per Play and genuine Arrow Barrage Dodge responses.");
        }
        else if (!TargetTrigger(p.Id, t) || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
            t.CardKinds.Count != 0 || p.ViewAs.Count != 0 || p.Activations.Count != 0 || p.CardPolicies is not [var policy] ||
            policy.Kind != SkillProgramCardPolicyKind.FactionHandLimitBonus || policy.FactionId != "qun" || policy.OwnerRole != Role.Lord || policy.Value != 2)
            throw new InvalidOperationException(path + ": Xueyi requires the native Lord population policy and actual other-Qun designations.");
    }
}
