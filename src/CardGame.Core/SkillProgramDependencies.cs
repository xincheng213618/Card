using System.Collections.Frozen;

namespace CardGame.Core;

/// <summary>
/// Whole-catalog skill/program facts compiled once after registry freezing. They deliberately
/// include programs not owned or currently enabled in a match: narrowing that
/// scope would change historical nullable facts and emitted event shapes.
/// </summary>
internal sealed class SkillProgramDependencies
{
    private readonly FrozenSet<SkillProgramTriggerConditionKind> _conditions;
    private readonly FrozenSet<SkillProgramTriggerValueKind> _values;
    private readonly FrozenSet<SkillProgramEffectOp> _activationOperations;
    private readonly FrozenDictionary<SkillProgramEffectOp, IReadOnlyList<string>> _triggerOperationSkillIds;
    private readonly FrozenSet<SkillProgramTriggerWindow> _windows;
    private readonly FrozenSet<SkillProgramCardPolicyKind> _cardPolicyKinds;
    private readonly FrozenSet<(CardKind Kind, SkillProgramCardCategory Category)> _finalizedCards;
    private readonly int? _maximumCardPolicyKind;

    internal SkillProgramDependencies(IEnumerable<ContentSkillDefinition> skills)
    {
        var definitions = skills.ToArray();
        var programs = definitions.Select(skill => skill.Program).OfType<SkillProgram>().ToArray();
        var triggers = programs.SelectMany(program => program.Triggers).ToArray();
        HpSensitiveSkillIds = definitions.Where(skill => skill.SuppressionRule is not null || skill.Program?.HpSkillGrants.Count > 0)
            .Select(skill => skill.Id).ToFrozenSet(StringComparer.Ordinal);
        HpConditionalCapabilitySkillIds = definitions.Where(skill => skill.Program?.HpSkillGrants.Count > 0)
            .Select(skill => skill.Id).ToFrozenSet(StringComparer.Ordinal);
        EquipmentSuitCapabilitySkillIds = definitions.Where(skill => skill.Program?.EquipmentSuitSkillGrants.Count > 0)
            .Select(skill => skill.Id).ToFrozenSet(StringComparer.Ordinal);
        EquipmentPresenceSkillIds = definitions.Where(skill => skill.Program is { } p &&
            (p.Modifiers.Any(m => m.Condition.ContainsEquipmentPresence()) || p.CardPolicies.Any(c => c.Condition.ContainsEquipmentPresence()) ||
             p.ViewAs.Any(v => v.Condition.ContainsEquipmentPresence()) ||
             p.Activations.Any(a => a.Condition.ContainsEquipmentPresence() || a.Effects.Any(e => e.Condition.ContainsEquipmentPresence())) ||
             p.Triggers.Any(t => t.Effects.Any(e => e.Condition.ContainsEquipmentPresence()))))
            .Select(skill => skill.Id).ToFrozenSet(StringComparer.Ordinal);
        UsesLordSkillProjection = programs.Any(program => program.LordSkillProjection);
        UsesGainPhaseQualification = triggers.Any(trigger => trigger.GainPhaseQualification is not null);
        UsesAnyLivingTurnStart = triggers.Any(trigger => trigger.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            trigger.TurnOwnerScope is SkillProgramTurnOwnerScope.AnyLiving or SkillProgramTurnOwnerScope.OtherLiving);
        UsesOwnerMarkerCount = programs.Any(program => program.Modifiers.Any(modifier => modifier.ValueExpression == SkillRuleValueExpression.OwnerMarkerCount));
        var resolver = ProgramInstructionResolver.Default;
        _conditions = triggers.SelectMany(trigger => resolver.Features(trigger).ConditionKinds).ToFrozenSet();
        TracksActualTurnUseOrdinal = UsesTriggerCondition(SkillProgramTriggerConditionKind.CardActionActualTurnUseOrdinalIs) ||
            triggers.Any(trigger => trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ResolveCuipoActualTurnUse));
        UsesLeiluanBasicUse = programs.Any(program => program.ViewAs.Any(rule => rule.LeiluanBasicUse is not null));
        UsesEqualNameLengthUse = programs.Any(program => program.ViewAs.Any(rule => rule.EqualNameLengthUse));
        UsesRankTwoDamageUse = programs.Any(program => program.ViewAs.Any(rule => rule.RankTwoDamageUse));
        TracksCurrentRoundActualUses = UsesLeiluanBasicUse || programs.Any(program => program.Modifiers.Any(modifier =>
            modifier.ValueExpression == SkillRuleValueExpression.NegatedCurrentRoundActualUseCount));
        HasWholeActualTurnTrickDamageModifiers = programs.Any(program => program.DamageModifiers.Any(modifier =>
            modifier.Condition == SkillProgramDamageModifierCondition.OwnerActualTurnTrickUseOrdinalWithinLimit));
        HasPublicEnemyParticipantDamageModifiers = programs.Any(program => program.DamageModifiers.Any(modifier =>
            modifier.SourceScope == SkillProgramDamageModifierSourceScope.AllLivingSkillOwners));
        TracksWholeActualTurnUseOrdinal = HasWholeActualTurnTrickDamageModifiers || triggers.Any(trigger => trigger.Effects.Any(effect =>
            effect.Op is SkillProgramEffectOp.ResolveBingzhongExtraTargets or SkillProgramEffectOp.ResolveMouHuangYueyingBingcai));
        TracksCurrentTurnUseKinds = UsesLeiluanBasicUse || TracksActualTurnUseOrdinal || TracksCurrentRoundActualUses || TracksWholeActualTurnUseOrdinal || programs.Any(program =>
            program.Modifiers.Any(modifier => modifier.ValueExpression == SkillRuleValueExpression.CurrentTurnUsedHandSuitCount) ||
            program.Triggers.SelectMany(trigger => trigger.Effects).Any(effect => effect.NumberExpression == SkillProgramNumberExpression.CurrentTurnUsedCardCategoryCount)) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.TurnOwnerUsedSameSuitCards);
        _triggerOperationSkillIds = definitions.Where(skill => skill.Program is not null)
            .SelectMany(skill => skill.Program!.Triggers.SelectMany(trigger => trigger.Effects)
                .Select(effect => (effect.Op, SkillId: skill.Id)))
            .GroupBy(entry => entry.Op).ToFrozenDictionary(group => group.Key,
                group => (IReadOnlyList<string>)Array.AsReadOnly(group.Select(entry => entry.SkillId)
                    .Distinct(StringComparer.Ordinal).ToArray()));
        _values = triggers.SelectMany(trigger => resolver.Features(trigger).ValueKinds)
            .Concat(HasTriggerOperation(SkillProgramEffectOp.AddEndingTurnDamageMarker)
                ? new[] { SkillProgramTriggerValueKind.TurnOwnerDamageDealtThisTurn }
                : Array.Empty<SkillProgramTriggerValueKind>()).ToFrozenSet();
        UsesActualTurnDeathBenefits = UsesTriggerValue(SkillProgramTriggerValueKind.ActualTurnDeathCount) ||
            triggers.Any(trigger => trigger.Effects.Any(e => e.NumberExpression == SkillProgramNumberExpression.ActualTurnDeathBenefits));
        _activationOperations = programs.SelectMany(program => program.Activations)
            .SelectMany(activation => activation.Effects).Select(effect => effect.Op).ToFrozenSet();
        _windows = triggers.Select(trigger => trigger.Window).ToFrozenSet();
        _cardPolicyKinds = programs.SelectMany(program => program.CardPolicies).Select(policy => policy.Kind).ToFrozenSet();
        UsesActualPlayPhaseUseLedger = HasCardPolicy(SkillProgramCardPolicyKind.FirstActualPlayUseDistanceUnlimited) ||
            HasCardPolicy(SkillProgramCardPolicyKind.StopUsingAfterTrickOrHpCount) ||
            _triggerOperationSkillIds.Keys.Concat(_activationOperations).Any(op =>
                ProgramOperationCatalog.Default.Resolve(op) is IActualPlayPhaseUseLedgerOperation);
        UsesRoundOnceCardlessBasics = programs.Any(program => program.ViewAs.Any(rule => rule.RoundOnceCardlessBasic is not null));
        UsesTieredRoundConversions = programs.Any(program => program.ViewAs.Any(rule => rule.TieredRoundConversion is not null));
        UsesRoundDistinctBasicUse = programs.Any(program => program.ViewAs.Any(rule => rule.RoundDistinctBasicUse is not null));
        UsesOwnPublicPileConversions = programs.Any(program => program.ViewAs.Any(rule => rule.SourcePileSkillId is not null));
        UsesRoundGainedRedHandOnly = programs.Any(program => program.ViewAs.Any(rule => rule.RoundGainedRedHandOnly == true));
        UsesDynamicRoundUsage = triggers.Any(trigger => trigger.DynamicUsageLimit is not null);
        UsesRoundTracking = HasTriggerOperation(SkillProgramEffectOp.ResolveJiguDamageEquipmentDraw) || HasCardPolicy(SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss) || UsesLeiluanBasicUse || HasTriggerWindow(SkillProgramTriggerWindow.RoundStarting) || HasTriggerWindow(SkillProgramTriggerWindow.RoundEnding) || HasTriggerOperation(SkillProgramEffectOp.ResolveSayingPlayStartObtainAndReply) || HasTriggerOperation(SkillProgramEffectOp.ResolveFengweiRoundStartDraw) || HasTriggerOperation(SkillProgramEffectOp.ResolveZonghuRequiredUseRequest) || HasActivationOperation(SkillProgramEffectOp.ResolveZonghuRequiredUseRequest) || HasActivationOperation(SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse) || HasTriggerOperation(SkillProgramEffectOp.ResolveGongjieRoundFirstGifts) || HasTriggerOperation(SkillProgramEffectOp.ResolveQiejianLastHandLoss) || HasTriggerOperation(SkillProgramEffectOp.ResolveHuiyunCompletedFireAttack) || HasTriggerOperation(SkillProgramEffectOp.ArmHanyongNativeGroupDamage) || TracksCurrentRoundActualUses || HasTriggerOperation(SkillProgramEffectOp.ResolveMiuyanCompletedUse) || HasTriggerOperation(SkillProgramEffectOp.ResolveXianyingDrawDiscard) || HasTriggerOperation(SkillProgramEffectOp.GiveCardSkipDrawForEnding) || HasTriggerOperation(SkillProgramEffectOp.DrawDyingExitRoundDistance) || HasTriggerOperation(SkillProgramEffectOp.TransferQuxiAtRoundOrDeath) || UsesTriggerValue(SkillProgramTriggerValueKind.RoundTurnSeatCount) || HasTriggerOperation(SkillProgramEffectOp.HoldRoundFocusCards) || HasTriggerOperation(SkillProgramEffectOp.AdjustOneRoundGainedOrdinaryTrickTarget) || HasTriggerOperation(SkillProgramEffectOp.BeginPhaseNamePrediction) || HasTriggerOperation(SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach) || UsesTieredRoundConversions || UsesRoundOnceCardlessBasics || HasTriggerWindow(SkillProgramTriggerWindow.TurnBeforeStart) || UsesRoundDistinctBasicUse || UsesRoundGainedRedHandOnly || UsesDynamicRoundUsage || HasActivationOperation(SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund) || HasTriggerOperation(SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol) || HasTriggerOperation(SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal) || HasTriggerOperation(SkillProgramEffectOp.ZecaiRoundSettlement) || HasTriggerOperation(SkillProgramEffectOp.FengjiRoundChoice) || HasTriggerOperation(SkillProgramEffectOp.KanggeHealVictim) || HasTriggerOperation(SkillProgramEffectOp.QualifyFirstRoundDamage) || HasTriggerOperation(SkillProgramEffectOp.ArmHongjiNextPhase);
        _maximumCardPolicyKind = _cardPolicyKinds.Select(kind => (int?)kind).Max();
        var finalized = triggers.Where(trigger => trigger.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized).ToArray();
        _finalizedCards = (from kind in Enum.GetValues<CardKind>()
            from category in Enum.GetValues<SkillProgramCardCategory>()
            where finalized.Any(trigger =>
                (trigger.CardKinds.Count == 0 || trigger.CardKinds.Contains(kind)) &&
                (trigger.CardCategories.Count == 0 || trigger.CardCategories.Contains(category)))
            select (kind, category)).ToFrozenSet();
        CapturesCompletedResponseSuit = triggers.Any(trigger =>
            trigger.Window == SkillProgramTriggerWindow.CardUseCompleted && trigger.IncludeResponseUses &&
            resolver.Features(trigger).UsesCondition(SkillProgramTriggerConditionKind.CardActionSuitIs));
        TracksPlayCardHistory = HasTriggerOperation(SkillProgramEffectOp.BeginPhaseNamePrediction) || HasTriggerOperation(SkillProgramEffectOp.ReplaceAllSlashTargets) ||
            HasTriggerOperation(SkillProgramEffectOp.GrantRandomSkillAndSuitShield) ||
            programs.Any(program => program.ViewAs.Any(rule => rule.InheritPreviousPlaySuit)) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.CardActionMatchesPreviousPlayCard) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.CardActionSuitIs) ||
            UsesTriggerCondition(SkillProgramTriggerConditionKind.PreviousPlayCardIsBasic) ||
            programs.Any(program => program.Modifiers.Any(item => item.Condition.ContainsPreviousPlayCardIsBasic()) ||
                program.CardPolicies.Any(item => item.Condition.ContainsPreviousPlayCardIsBasic()));
    }

    internal IReadOnlySet<string> EquipmentPresenceSkillIds { get; }
    internal IReadOnlySet<string> EquipmentSuitCapabilitySkillIds { get; }
    internal bool UsesOwnerMarkerCount { get; }
    internal IReadOnlySet<string> HpSensitiveSkillIds { get; }
    internal IReadOnlySet<string> HpConditionalCapabilitySkillIds { get; }
    internal bool UsesLordSkillProjection { get; }
    internal bool UsesGainPhaseQualification { get; }
    internal bool UsesAnyLivingTurnStart { get; }
    internal bool UsesActualTurnDeathBenefits { get; }
    internal bool UsesActualPlayPhaseUseLedger { get; }
    internal bool TracksCurrentTurnUseKinds { get; }
    internal bool TracksActualTurnUseOrdinal { get; }
    internal bool TracksCurrentRoundActualUses { get; }
    internal bool TracksWholeActualTurnUseOrdinal { get; }
    internal bool HasWholeActualTurnTrickDamageModifiers { get; }
    internal bool HasPublicEnemyParticipantDamageModifiers { get; }
    internal bool UsesDynamicRoundUsage { get; }
    internal bool UsesTieredRoundConversions { get; }
    internal bool UsesLeiluanBasicUse { get; }
    internal bool UsesEqualNameLengthUse { get; }
    internal bool UsesRankTwoDamageUse { get; }
    internal bool UsesRoundOnceCardlessBasics { get; }
    internal bool UsesRoundDistinctBasicUse { get; }
    internal bool UsesRoundGainedRedHandOnly { get; }
    internal bool UsesOwnPublicPileConversions { get; }
    internal bool UsesRoundTracking { get; }
    internal bool CapturesCompletedResponseSuit { get; }
    internal bool TracksPlayCardHistory { get; }
    internal bool UsesTriggerCondition(SkillProgramTriggerConditionKind kind) => _conditions.Contains(kind);
    internal bool UsesTriggerValue(SkillProgramTriggerValueKind kind) => _values.Contains(kind);
    internal bool HasTriggerOperation(SkillProgramEffectOp op) => _triggerOperationSkillIds.ContainsKey(op);
    internal bool HasActivationOperation(SkillProgramEffectOp op) => _activationOperations.Contains(op);
    internal IReadOnlyList<string> GetTriggerOperationSkillIds(SkillProgramEffectOp op) =>
        _triggerOperationSkillIds.GetValueOrDefault(op) ?? Array.Empty<string>();
    internal bool HasTriggerWindow(SkillProgramTriggerWindow window) => _windows.Contains(window);
    internal bool HasCardPolicy(SkillProgramCardPolicyKind kind) => _cardPolicyKinds.Contains(kind);
    internal bool HasFinalizedCardTrigger(CardKind kind, SkillProgramCardCategory category) => _finalizedCards.Contains((kind, category));
    internal bool HasCardPolicyKindAtOrAbove(int minimum) => _maximumCardPolicyKind >= minimum;
}
