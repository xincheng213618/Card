using System.Text.Json;
using System.Text.Json.Nodes;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ActivityOrdinaryTricks
{
    internal static string ActivityViewAsFamily(SkillProgramViewAs rule)
    {
        // Every cost, condition, usage and tier property participates. A lone
        // named conversion, or an incomplete family, cannot acquire new names.
        var node = JsonNode.Parse(JsonSerializer.Serialize(rule))!.AsObject();
        node.Remove(nameof(SkillProgramViewAs.Id)); node.Remove(nameof(SkillProgramViewAs.OutputKind));
        return node.ToJsonString();
    }

    internal static SkillProgramViewAs CloneActivityViewAs(SkillProgramViewAs r, CardKind kind, string? bindingId = null, bool? forResponse = null) =>
        new(bindingId ?? r.Id, r.InputKinds, r.InputSuits, kind, r.ForPlay, forResponse ?? r.ForResponse, r.Condition, r.InputCount,
            r.SourceZones, r.AllowChainedInput, r.InputCategories, r.SameSuit, r.UsesPerPhase, r.UsageGroup, r.InheritPreviousPlaySuit)
        {
            InputCountExpression = r.InputCountExpression, UsesPerActualTurn = r.UsesPerActualTurn, KenshangUse = r.KenshangUse, VariableInputCount = r.VariableInputCount,
            AllHandCardsOfSelectedSuit = r.AllHandCardsOfSelectedSuit, DistanceUnlimited = r.DistanceUnlimited,
            EqualNameLengthUse = r.EqualNameLengthUse,
            RankTwoDamageUse = r.RankTwoDamageUse,
            NonBasicUnrespondableSlash = r.NonBasicUnrespondableSlash, UseEffectiveInputSuit = r.UseEffectiveInputSuit,
            ExtendedUse = r.ExtendedUse, DamageBonus = r.DamageBonus, RecoveryBonus = r.RecoveryBonus,
            CostDestination = r.CostDestination, UnusedOutputThisTurn = r.UnusedOutputThisTurn, UseOnly = r.UseOnly,
            AllowSameKind = r.AllowSameKind, NoDying = r.NoDying, UnusedOutputNameThisGame = r.UnusedOutputNameThisGame,
            NameLedgerId = r.NameLedgerId, SingleCardTrickUse = r.SingleCardTrickUse, ExcludeOwnerEffects = r.ExcludeOwnerEffects,
            ConversionStateId = r.ConversionStateId, MinimumTier = r.MinimumTier, MaximumTier = r.MaximumTier,
            DeclarationValidation = r.DeclarationValidation, DeclaredEntity = r.DeclaredEntity, ActivationUsageGroup = r.ActivationUsageGroup,
            TieredRoundConversion = r.TieredRoundConversion, RoundOnceCardlessBasic = r.RoundOnceCardlessBasic,
            LeiluanBasicUse = r.LeiluanBasicUse, ChainedStateCost = r.ChainedStateCost, RoundDistinctBasicUse = r.RoundDistinctBasicUse,
            DrawFundedDistinctBasic = r.DrawFundedDistinctBasic, DisableSkillUntilTurnEndIfNoDamage = r.DisableSkillUntilTurnEndIfNoDamage,
            LastInSourceZone = r.LastInSourceZone, SourcePileSkillId = r.SourcePileSkillId, RoundGainedRedHandOnly = r.RoundGainedRedHandOnly
        };

    internal static SkillProgramTrigger CloneActivityTrigger(SkillProgramTrigger t, IReadOnlyList<CardKind> kinds) =>
        new(t.Id, t.Window, t.SourceSkillId, t.SourceViewAsId, t.Subject, t.Suits, t.MinimumRank, t.MaximumRank,
            t.ExcludedReasons, t.JudgmentReasons, t.JudgmentSource, kinds, t.SourceZones, t.MovementOccurrence,
            t.DamageOccurrence, t.DrawPhaseMode, t.Optional, t.Condition, t.Effects, t.Priority, t.UsageScope, t.UsageLimit,
            t.ChoiceGroup, t.OwnerRelation, t.CardCategories, t.DamageCardKinds, t.TurnOwnerScope)
        {
            DestinationZones = t.DestinationZones, MovementReasons = t.MovementReasons, ExcludedMovementReasons = t.ExcludedMovementReasons,
            IgnoreOwnSkillMovements = t.IgnoreOwnSkillMovements, MovementDiscardOnly = t.MovementDiscardOnly,
            DiscardOwnerScope = t.DiscardOwnerScope, HpChangeOccurrence = t.HpChangeOccurrence, DynamicUsageLimit = t.DynamicUsageLimit,
            NamedUsageGroup = t.NamedUsageGroup, GainPhaseQualification = t.GainPhaseQualification, RequireDamageSource = t.RequireDamageSource,
            RequireNoCardConversion = t.RequireNoCardConversion, RequiredPolarity = t.RequiredPolarity, OrderGroup = t.OrderGroup, AllowNoEventTarget = t.AllowNoEventTarget,
            IncludeResponseUses = t.IncludeResponseUses, SingleActionInstance = t.SingleActionInstance, NoDyingAtActivation = t.NoDyingAtActivation,
            DeferredTurnEndOnly = t.DeferredTurnEndOnly, OnlyDesignatedCardTargets = t.OnlyDesignatedCardTargets,
            AllowOwnDiscardPhaseEnded = t.AllowOwnDiscardPhaseEnded, EvaluateConditionAtResolution = t.EvaluateConditionAtResolution,
            ChoiceLabel = t.ChoiceLabel, MarkerCost = t.MarkerCost
        };
}
