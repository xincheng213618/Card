namespace CardGame.Core;

internal static class LeCaoZhiComposition
{
    private static readonly CardKind[] FuKinds =
    [
        CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol, CardKind.FireSlash, CardKind.ThunderSlash,
        CardKind.DrawTwo, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.PeachGarden,
        CardKind.FiveGrains, CardKind.Dismantlement, CardKind.Snatch, CardKind.QizhengXiangsheng, CardKind.FireAttack, CardKind.IronChain,
        CardKind.BorrowedSword, CardKind.WumiaoFlood, CardKind.Nullification, CardKind.Indulgence,
        CardKind.SupplyShortage, CardKind.Lightning, CardKind.UnexpectedAssault
    ];

    [ProgramCatalogValidation(920)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        var operations = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects));
        if (p.Id is not ("le:fule" or "le:wenlan") && !p.ViewAs.Any(r => r.Id.StartsWith("fu-alias-", StringComparison.Ordinal)) &&
            !operations.Any(e => e.Op is SkillProgramEffectOp.InitializeLeCaoZhiFule or SkillProgramEffectOp.ResolveLeCaoZhiWenlan)) return;
        if (!IsFuleProgram(p) && !IsWenlanProgram(p))
            throw new InvalidOperationException(path + ": Le Cao Zhi requires its complete source-bound physical Fu or native paired-action program.");
    }

    internal static bool IsFuleProgram(SkillProgram p) => p.Id == "le:fule" && PlainProgram(p) &&
        p.Triggers is [var t] && t.Id == "mark-initial-entities" && PlainTrigger(t) &&
        t.Window == SkillProgramTriggerWindow.GameStarting && t.Subject == SkillProgramTriggerSubject.Owner &&
        t.OwnerRelation is null && !t.IncludeResponseUses && OwnEffect(t, SkillProgramEffectOp.InitializeLeCaoZhiFule) &&
        p.ViewAs.Count == FuKinds.Length && p.ViewAs.All(IsFuleRule) &&
        p.ViewAs.Select(r => r.OutputKind).ToHashSet().SetEquals(FuKinds);

    internal static bool IsWenlanProgram(SkillProgram p) => p.Id == "le:wenlan" && PlainProgram(p) &&
        p.ViewAs.Count == 0 && p.Triggers.Count == 2 && p.Triggers.All(IsWenlanTrigger) &&
        p.Triggers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal).SetEquals(["after-pair-use", "after-pair-played"]);

    internal static bool IsWenlanTrigger(SkillProgramTrigger t) => PlainTrigger(t) &&
        t.Subject is null && t.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor &&
        OwnEffect(t, SkillProgramEffectOp.ResolveLeCaoZhiWenlan) &&
        (t.Id == "after-pair-use" && t.Window == SkillProgramTriggerWindow.CardUseCompleted && t.IncludeResponseUses ||
         t.Id == "after-pair-played" && t.Window == SkillProgramTriggerWindow.CardResponseCompleted && !t.IncludeResponseUses);

    internal static bool IsFuleRule(SkillProgramViewAs r)
    {
        var name = r.OutputKind.ToString();
        var trick = r.OutputKind is CardKind.DrawTwo or CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
            CardKind.PeachGarden or CardKind.FiveGrains or CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
            CardKind.IronChain or CardKind.BorrowedSword or CardKind.WumiaoFlood or CardKind.QizhengXiangsheng;
        var delayed = r.OutputKind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning;
        return FuKinds.Contains(r.OutputKind) && r.Id == "fu-alias-" + char.ToLowerInvariant(name[0]) + name[1..] &&
            r.ForPlay == (r.OutputKind is not (CardKind.Dodge or CardKind.Nullification)) &&
            r.ForResponse == (r.OutputKind is CardKind.Dodge or CardKind.Nullification or CardKind.Peach or CardKind.Alcohol or
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) &&
            r.InputCount == 1 && r.InputKinds.Count == 0 && r.InputSuits.Count == 0 && r.InputCategories.Count == 0 &&
            r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.WoodenOxGrain]) &&
            r.Condition.Kind == SkillProgramConditionKind.Always && r.AllowSameKind && r.SingleCardTrickUse == trick &&
            r.UseOnly == trick && r.ExtendedUse == delayed && !r.AllowChainedInput && !r.SameSuit &&
            r.UsesPerPhase is null && r.UsesPerActualTurn is null && r.UsageGroup is null && !r.InheritPreviousPlaySuit &&
            r.KenshangUse is null && !r.VariableInputCount && !r.AllHandCardsOfSelectedSuit && !r.DistanceUnlimited &&
            !r.EqualNameLengthUse && !r.RankTwoDamageUse && !r.NonBasicUnrespondableSlash && r.UseEffectiveInputSuit is null &&
            r.DamageBonus == 0 && r.RecoveryBonus == 0 && r.CostDestination is null && !r.UnusedOutputThisTurn && !r.NoDying &&
            !r.UnusedOutputNameThisGame && r.NameLedgerId is null && !r.ExcludeOwnerEffects && r.ConversionStateId is null &&
            r.MinimumTier == 0 && r.MaximumTier == 2 && r.DeclarationValidation is null && !r.DeclaredEntity &&
            r.ActivationUsageGroup is null && r.TieredRoundConversion is null && r.RoundOnceCardlessBasic is null &&
            r.LeiluanBasicUse is null && r.XieweiBaitBasicUse is null && r.ChainedStateCost is null &&
            r.RoundDistinctBasicUse is null && r.DrawFundedDistinctBasic is null && r.DisableSkillUntilTurnEndIfNoDamage is null &&
            r.LastInSourceZone is null && r.SourcePileSkillId is null && r.RoundGainedRedHandOnly is null;
    }

    private static bool PlainProgram(SkillProgram p) => p.Activations.Count == 0 && p.Modifiers.Count == 0 &&
        p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.HpSkillGrants.Count == 0 &&
        p.EquipmentSuitSkillGrants.Count == 0 && p.DamageModifiers.Count == 0 && p.BooleanStates.Count == 0 &&
        p.Contributions.Count == 0 && !p.LordSkillProjection && !p.CannotChallengeDeclarations;
    private static bool OwnEffect(SkillProgramTrigger t, SkillProgramEffectOp op) =>
        t.Effects is [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
    private static bool PlainTrigger(SkillProgramTrigger t) => !t.Optional &&
        t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Priority == 0 &&
        t.UsageLimit is null && t.UsageScope is null && t.DynamicUsageLimit is null && t.NamedUsageGroup is null &&
        t.MarkerCost is null && t.ChoiceGroup is null && t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.SourceSkillId is null && t.SourceViewAsId is null && t.CardKinds.Count == 0 && t.CardCategories.Count == 0 &&
        t.DamageCardKinds.Count == 0 && t.Suits.Count == 0 && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 &&
        t.MovementReasons.Count == 0 && t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 &&
        t.JudgmentReasons.Count == 0 && t.JudgmentSource is null && t.MinimumRank == 1 && t.MaximumRank == 13 &&
        t.DrawPhaseMode == SkillProgramDrawPhaseMode.Additive && t.MovementOccurrence is null && !t.MovementDiscardOnly &&
        !t.SingleActionInstance && !t.DeferredTurnEndOnly && !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null &&
        t.DamageOccurrence is null && !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded;
}
