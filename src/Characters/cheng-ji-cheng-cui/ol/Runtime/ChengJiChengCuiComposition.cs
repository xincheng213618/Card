namespace CardGame.Core;

internal static class ChengJiChengCuiComposition
{
    internal const string Tousui = "ol:tousui", Xuming = "ol:xuming";
    internal static bool IsBottomCostSlash(SkillProgramViewAs r) =>
        r.CostDestination == SkillProgramCardDestination.DrawPileBottom && r.VariableInputCount && r.InputCount == 1 &&
        r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) && r.OutputKind == CardKind.Slash &&
        r.ForPlay && !r.ForResponse && r.UseOnly && r.AllowSameKind && r.InputKinds.Count == 0 && r.InputSuits.Count == 0 &&
        r.InputCategories.Count == 0 && !r.SameSuit && !r.AllowChainedInput && !r.ExtendedUse && !r.SingleCardTrickUse &&
        !r.DistanceUnlimited && !r.AllHandCardsOfSelectedSuit && r.KenshangUse is null && r.DeclarationValidation is null &&
        r.UsesPerPhase is null && r.UsesPerActualTurn is null && r.ConversionStateId is null && r.ActivationUsageGroup is null &&
        !r.InheritPreviousPlaySuit && !r.NoDying && !r.ExcludeOwnerEffects && !r.UnusedOutputThisTurn &&
        !r.UnusedOutputNameThisGame && r.NameLedgerId is null && r.UsageGroup is null && !r.DeclaredEntity &&
        r.TieredRoundConversion is null && r.RoundOnceCardlessBasic is null && r.DrawFundedDistinctBasic is null &&
        r.RoundDistinctBasicUse is null && r.ChainedStateCost is null && r.LastInSourceZone is null &&
        r.SourcePileSkillId is null && r.RoundGainedRedHandOnly is null && r.DisableSkillUntilTurnEndIfNoDamage is null &&
        !r.RankTwoDamageUse && !r.EqualNameLengthUse && !r.NonBasicUnrespondableSlash && r.XieweiBaitBasicUse is null &&
        r.LeiluanBasicUse is null && r.UseEffectiveInputSuit is null &&
        r.DamageBonus == 0 && r.RecoveryBonus == 0 && r.Condition.Kind == SkillProgramConditionKind.Always;

    [ProgramCatalogValidation(1440)]
    internal static void Validate(string path, SkillProgram p)
    {
        var reserved = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e =>
            e.Op is SkillProgramEffectOp.ResolveTousuiDodgeRequirement or SkillProgramEffectOp.ResolveXumingDamagePromise or SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses);
        if (p.Id is not (Tousui or Xuming))
        { if (reserved) throw new InvalidOperationException(path + ": Cheng Ji Cheng Cui's issued operations cannot be transplanted."); return; }
        if (p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Cheng Ji Cheng Cui requires the complete native two-skill contract.");
        if (p.Id == Tousui)
        {
            if (p.ViewAs is not [var r] || r.Id != "bottom-cost-slash" || !IsBottomCostSlash(r) ||
                p.Activations is not [var a] || a.Id != "native-bottom-slash" || a.MinCards != 1 || a.MaxCards != int.MaxValue ||
                !a.SourceZones.SequenceEqual(r.SourceZones) || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
                a.UsesPerPhase is not null || a.UsesPerTurn is not null || a.Effects is not [{ Op: SkillProgramEffectOp.UseSelectedCardsAs,
                    SourceBind: "bottom-cost-slash", OutputKind: CardKind.Slash, Target: SkillProgramEffectTarget.SelectedTarget }] ||
                p.Triggers is not [var t] || t.Id != "paid-dodge-requirement" || t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
                t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.Optional ||
                t.SourceSkillId != Tousui || t.SourceViewAsId != r.Id || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveTousuiDodgeRequirement }])
                throw new InvalidOperationException(path + ": Tousui requires its real variable HE bottom cost, ordinary native Slash and exact paid response requirement.");
            return;
        }
        if (p.ViewAs.Count != 0 || p.Activations.Count != 0 || p.Triggers.Count != 3 ||
            !p.Triggers.Select(t => t.Id).SequenceEqual(["outgoing-damage-promise", "incoming-damage-promise", "issued-whole-turn-uses"]))
            throw new InvalidOperationException(path + ": Xuming requires both native damage directions and its issued whole-turn completion.");
        foreach (var t in p.Triggers)
        {
            var ending = t.Id == "issued-whole-turn-uses";
            if (t.Optional || t.UsageScope is not null || t.UsageLimit is not null || t.Effects is not [var e] ||
                e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
                (ending ? t.Window != SkillProgramTriggerWindow.AfterTurnEnded || t.Subject != SkillProgramTriggerSubject.Owner ||
                    t.TurnOwnerScope != SkillProgramTurnOwnerScope.AnyLiving || e.Op != SkillProgramEffectOp.ResolveXumingIssuedTurnEndUses :
                    t.Window != SkillProgramTriggerWindow.BeforeDamageApplied || e.Op != SkillProgramEffectOp.ResolveXumingDamagePromise ||
                    t.Subject != (t.Id == "outgoing-damage-promise" ? SkillProgramTriggerSubject.DamageSource : SkillProgramTriggerSubject.DamageTarget)))
                throw new InvalidOperationException(path + ": Xuming cannot alter its damage/physical-material/actual-ended-turn contract.");
        }
    }
}
