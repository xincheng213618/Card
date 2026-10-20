namespace CardGame.Core;

internal static class MouZhuRongComposition
{
    internal const string Lieren = "mou-mobile:lieren", Juxiang = "mou-mobile:juxiang";
    [ProgramCatalogValidation(1070)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var reserved = p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.RecordMouZhuRongLieren or
            SkillProgramEffectOp.SelectMouZhuRongLierenTarget or SkillProgramEffectOp.GiftMouZhuRongOutsideAssault);
        if (p.Id is not (Lieren or Juxiang))
        { if (reserved) throw new InvalidOperationException(path + ": mobile Zhu Rong's native bindings cannot be transplanted."); return; }
        var slash = new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash };
        var valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.BooleanStates.Count == 0;
        if (p.Id == Lieren)
            valid &= p.CardPolicies.Count == 0 && p.Triggers is [var contest, var damage] &&
                contest.Id == "unique-slash-contest" && contest.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                contest.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && contest.Optional && contest.CardKinds.SequenceEqual(slash) &&
                contest.Effects is [{ Op: SkillProgramEffectOp.StartCardActionPindian, ResultBind: "contest", Visibility: SkillProgramCardSetVisibility.Public, OpponentReference.Kind: ProgramParticipantRef.EventTarget },
                    { Op: SkillProgramEffectOp.RecordMouZhuRongLieren, Target: SkillProgramEffectTarget.Owner }] &&
                damage.Id == "won-slash-damage" && damage.Window == SkillProgramTriggerWindow.CardUseCompleted &&
                damage.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && damage.Optional && damage.CardKinds.SequenceEqual(slash) &&
                damage.Effects is [{ Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.MouZhuRongLierenOther },
                    { Op: SkillProgramEffectOp.SelectMouZhuRongLierenTarget, Target: SkillProgramEffectTarget.Owner },
                    { Op: SkillProgramEffectOp.Damage, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1 }];
        else
            valid &= p.Triggers is [var gift] && gift.Id == "outside-assault-gift" && gift.Window == SkillProgramTriggerWindow.TurnEnding &&
                !gift.Optional && gift.Effects is [{ Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.AnyLiving },
                    { Op: SkillProgramEffectOp.GiftMouZhuRongOutsideAssault, Target: SkillProgramEffectTarget.Owner }] &&
                p.CardPolicies.Count == 2 && p.CardPolicies.Any(c => c.Kind == SkillProgramCardPolicyKind.ExcludeGlobalTarget && c.CardKinds.SequenceEqual([CardKind.BarbarianAssault])) &&
                p.CardPolicies.Any(c => c.Kind == SkillProgramCardPolicyKind.ClaimResolvedGlobalCard && c.CardKinds.SequenceEqual([CardKind.BarbarianAssault]));
        if (!valid || p.Triggers.Any(t => t.UsageLimit is not null || t.UsageScope is not null || t.SingleActionInstance || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path + ": mobile Zhu Rong requires the complete designated Slash contest, deferred damage, and outside Assault gift bindings.");
    }
}
