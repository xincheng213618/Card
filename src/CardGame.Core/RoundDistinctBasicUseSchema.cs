using System.Text.Json;
using System.Text.RegularExpressions;

namespace CardGame.Core;

internal static class RoundDistinctBasicUseSchema
{
    internal static ProgramRoundDistinctBasicUsePolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 2 ||
            node.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
            node.EnumerateObject().Any(p => p.Name is not ("stateId" or "ledgerId")) ||
            !node.TryGetProperty("stateId", out var state) || !ValidId(state) ||
            !node.TryGetProperty("ledgerId", out var ledger) || !ValidId(ledger))
            throw new InvalidOperationException(path + ": requires only bounded stateId and ledgerId.");
        return new(state.GetString()!, ledger.GetString()!);
    }

    private static bool ValidId(JsonElement node) => node.ValueKind == JsonValueKind.String &&
        node.GetString() is { Length: > 0 and <= 128 } value &&
        Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]*\z");

    internal static void Validate(SkillProgramViewAs rule, string path)
    {
        if (rule.RoundDistinctBasicUse is null) return;
        var dodge = rule.OutputKind == CardKind.Dodge;
        if (rule.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                CardKind.Dodge or CardKind.Peach or CardKind.Alcohol) ||
            rule.InputCount != 2 || !rule.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            rule.InputKinds.Count != 0 || rule.InputSuits.Count != 0 || rule.InputCategories.Count != 0 ||
            !rule.UseOnly || !rule.ExtendedUse || rule.ForPlay == dodge || rule.ForResponse != dodge ||
            !rule.AllowSameKind || rule.NoDying || rule.Condition.Kind != SkillProgramConditionKind.Always ||
            rule.TieredRoundConversion is not null || rule.DrawFundedDistinctBasic is not null ||
            rule.ConversionStateId is not null || rule.MinimumTier != 0 || rule.MaximumTier != 2 ||
            rule.CostDestination is not null || rule.UsesPerPhase is not null || rule.UsageGroup is not null ||
            rule.ActivationUsageGroup is not null || rule.UnusedOutputThisTurn || rule.UnusedOutputNameThisGame ||
            rule.NameLedgerId is not null || rule.SingleCardTrickUse || rule.ExcludeOwnerEffects ||
            rule.DeclaredEntity || rule.DeclarationValidation is not null || rule.AllowChainedInput ||
            rule.SameSuit || rule.InheritPreviousPlaySuit || rule.VariableInputCount || rule.DistanceUnlimited ||
            rule.DamageBonus != 0 || rule.RecoveryBonus != 0 || rule.UseEffectiveInputSuit is not null)
            throw new InvalidOperationException(path + ": requires exactly two unrestricted hand-like materials, a mature use-only basic direction and only its stable round-name policy.");
    }
}
