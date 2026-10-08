using System.Text.Json;
using System.Text.RegularExpressions;

namespace CardGame.Core;

/// <summary>Stable method identity; removing and reacquiring a skill does not create another allowance.</summary>
public sealed record DrawFundedDistinctBasicPolicy(string MethodLedgerId);

internal static class DrawFundedDistinctBasicSchema
{
    internal static DrawFundedDistinctBasicPolicy? Parse(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Null) return null;
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1 ||
            !node.TryGetProperty("methodLedgerId", out var ledger) || ledger.ValueKind != JsonValueKind.String ||
            ledger.GetString() is not { Length: > 0 and <= 128 } value ||
            !Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]*\z"))
            throw new InvalidOperationException(path + ": requires only one bounded methodLedgerId.");
        return new(value);
    }

    internal static void Validate(SkillProgramViewAs rule, string path)
    {
        if (rule.DrawFundedDistinctBasic is null) return;
        var dodge = rule.OutputKind == CardKind.Dodge;
        if (rule.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol) ||
            rule.InputCount != 0 || rule.SourceZones.Count != 0 || rule.InputKinds.Count != 0 || rule.InputSuits.Count != 0 || rule.InputCategories.Count != 0 ||
            !rule.UseOnly || rule.ForPlay == dodge || rule.ForResponse != dodge || rule.NoDying ||
            rule.Condition.Kind != SkillProgramConditionKind.Always || rule.TieredRoundConversion is not null || rule.ConversionStateId is not null ||
            rule.MinimumTier != 0 || rule.MaximumTier != 2 || rule.CostDestination is not null || rule.UsesPerPhase is not null || rule.UsageGroup is not null ||
            rule.ActivationUsageGroup is not null || rule.UnusedOutputThisTurn || rule.UnusedOutputNameThisGame || rule.NameLedgerId is not null ||
            rule.ExtendedUse || rule.SingleCardTrickUse || rule.ExcludeOwnerEffects || rule.DeclaredEntity || rule.DeclarationValidation is not null ||
            rule.AllowChainedInput || rule.SameSuit || rule.InheritPreviousPlaySuit || rule.VariableInputCount || rule.DistanceUnlimited ||
            rule.DamageBonus != 0 || rule.RecoveryBonus != 0 || rule.UseEffectiveInputSuit is not null || rule.AllowSameKind)
            throw new InvalidOperationException(path + ": requires an exact zero-material use-only basic direction without another condition, cost, tier or quota policy.");
    }
}
