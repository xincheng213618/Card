using System.Text.Json;

namespace CardGame.Core;

// Additive opt-in validation. The old positive-input validator remains the
// authority whenever this optional property is absent.
internal static class TieredRoundConversionSchema
{
    internal static ProgramTieredRoundConversionPolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 2 ||
            node.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
            node.EnumerateObject().Any(p => p.Name is not ("stateId" or "usageId")) ||
            !node.TryGetProperty("stateId", out var state) || state.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("usageId", out var usage) || usage.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(state.GetString()) || string.IsNullOrWhiteSpace(usage.GetString()) ||
            state.GetString()!.Length > 128 || usage.GetString()!.Length > 128 ||
            !System.Text.RegularExpressions.Regex.IsMatch(state.GetString()!, @"^[A-Za-z0-9][A-Za-z0-9_.:-]*$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(usage.GetString()!, @"^[A-Za-z0-9][A-Za-z0-9_.:-]*$"))
            throw new InvalidOperationException(path + ": requires only nonempty bounded stateId and usageId.");
        return new(state.GetString()!, usage.GetString()!);
    }

    internal static void Validate(SkillProgramViewAs rule, string path)
    {
        if (rule.TieredRoundConversion is not { } policy) return;
        var basic = CardUseCategoryCatalog.Get(rule.OutputKind) == CardUseCategories.Basic;
        var ordinary = CardUseCategoryCatalog.Get(rule.OutputKind) == CardUseCategories.InstantTrick;
        var response = rule.OutputKind is CardKind.Dodge or CardKind.Nullification;
        if (!basic && !ordinary || rule.ConversionStateId != policy.StateId || rule.MinimumTier != rule.MaximumTier ||
            rule.MinimumTier is < 0 or > 2 || rule.InputCount != (rule.MinimumTier == 2 ? 0 : 1) || !rule.UseOnly ||
            rule.ForPlay == response || rule.ForResponse != response ||
            rule.SingleCardTrickUse != (ordinary && !response) || rule.ExtendedUse || rule.VariableInputCount ||
            rule.DistanceUnlimited || rule.DeclaredEntity || rule.DeclarationValidation is not null || rule.ActivationUsageGroup is not null ||
            rule.UsesPerPhase is not null || rule.UsageGroup is not null || rule.CostDestination is not null ||
            rule.AllowChainedInput || rule.SameSuit || rule.InheritPreviousPlaySuit || rule.UnusedOutputThisTurn ||
            rule.UnusedOutputNameThisGame || rule.NameLedgerId is not null || rule.ExcludeOwnerEffects || rule.NoDying ||
            rule.DamageBonus != 0 || rule.RecoveryBonus != 0 ||
            rule.InputCount == 0 && (rule.SourceZones.Count != 0 || rule.InputKinds.Count != 0 || rule.InputSuits.Count != 0 || rule.InputCategories.Count != 0) ||
            rule.InputCount == 1 && (rule.SourceZones.Count == 0 || rule.SourceZones.Any(z => z is not (CardZoneKind.Hand or CardZoneKind.Equipment))))
            throw new InvalidOperationException(path + ": tiered round conversion requires an exact level, one HE material or genuine zero material, and a mature use-only basic/ordinary-trick direction without another usage or cost policy.");
    }
}
