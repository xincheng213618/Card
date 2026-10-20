namespace CardGame.Core;
internal static class MouPangTongComposition
{
    internal const string Lianhuan = "mou-mobile:lianhuan", Niepan = "mou-mobile:niepan";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ArmMouPangTongLianhuan or SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets or SkillProgramEffectOp.UpgradeMouPangTongLianhuan;
    private static bool Own(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.ActorReference is null && e.TargetReference is null && e.Condition.Kind == SkillProgramConditionKind.Always;
    [ProgramCatalogValidation(1030)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id is not (Lianhuan or Niepan) && !p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => Handles(e.Op))) return;
        var common = p.Activations.Count == 0 && p.Modifiers.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 && p.DamageModifiers.Count == 0;
        var valid = p.Id switch
        {
            Lianhuan => p.ViewAs is [var v] && v.Id == "club-hand-chain" && v.SourceZones.SequenceEqual([CardZoneKind.Hand]) && v.InputSuits.SequenceEqual([Suit.Club]) && v.OutputKind == CardKind.IronChain && v.ForPlay && !v.ForResponse && v.UsesPerPhase is null &&
                p.Triggers is [var paid, var targets] && paid.Id == "pay-for-chain" && paid.Window == SkillProgramTriggerWindow.CardUseCommitted && paid.Optional && paid.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && paid.CardKinds.SequenceEqual([CardKind.IronChain]) &&
                paid.Effects is [var loss, var arm] && Own(loss, SkillProgramEffectOp.LoseHp) && loss.Amount == 1 && Own(arm, SkillProgramEffectOp.ArmMouPangTongLianhuan) &&
                targets.Id == "discard-unchained-targets" && targets.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized && !targets.Optional && targets.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && targets.CardKinds.SequenceEqual([CardKind.IronChain]) && targets.Effects is [var discard] && Own(discard, SkillProgramEffectOp.ResolveMouPangTongLianhuanTargets),
            Niepan => p.ViewAs.Count == 0 && p.Triggers is [var t] && t.Id == "rebirth" && t.Window == SkillProgramTriggerWindow.SelfDyingResponse && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional && t.UsageScope == SkillUsageScope.Game && t.UsageLimit == 1 &&
                t.Effects is [var d, var draw, var recover, var chain, var face, var upgrade] && Own(d, SkillProgramEffectOp.DiscardOwnedZoneCards) && d.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
                Own(draw, SkillProgramEffectOp.Draw) && draw.Amount == 2 && Own(recover, SkillProgramEffectOp.RecoverTo) && recover.MinimumValue == 2 && recover.ClampToMaxHp &&
                Own(chain, SkillProgramEffectOp.SetChainedState) && chain.Chained == false && Own(face, SkillProgramEffectOp.SetFaceState) && face.FaceDown == false && Own(upgrade, SkillProgramEffectOp.UpgradeMouPangTongLianhuan),
            _ => false
        };
        if (!common || !valid) throw new InvalidOperationException(path + ": Mou Pang Tong requires the complete two-level native IronChain and ordered Draw2/RecoverTo2 rebirth contracts.");
    }
}
