using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CardGame.Core;

public sealed record ProgramLeiluanBasicUsePolicy(string StateId);
internal static class LeiluanBasicUseSchema
{
    internal static ProgramLeiluanBasicUsePolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1 ||
            !node.TryGetProperty("stateId", out var state) || state.ValueKind != JsonValueKind.String ||
            state.GetString() is not { Length: > 0 and <= 128 } id || !Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]*\z"))
            throw new InvalidOperationException(path + ": Leiluan requires exactly one bounded stateId.");
        return new(id);
    }
    internal static void Validate(SkillProgramViewAs r, string path)
    {
        if (r.LeiluanBasicUse is null) return;
        var dodge = r.OutputKind == CardKind.Dodge;
        if (r.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol) ||
            r.InputCount != 0 || !r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.WoodenOxGrain]) ||
            r.InputKinds.Count != 0 || r.InputSuits.Count != 0 || r.InputCategories.Count != 0 ||
            !r.UseOnly || !r.ExtendedUse || !r.AllowSameKind || r.ForPlay == dodge || r.ForResponse != dodge ||
            r.Condition.Kind != SkillProgramConditionKind.Always || r.TieredRoundConversion is not null || r.RoundDistinctBasicUse is not null ||
            r.DrawFundedDistinctBasic is not null || r.RoundOnceCardlessBasic is not null || r.ChainedStateCost is not null ||
            r.RoundGainedRedHandOnly is not null || r.ConversionStateId is not null || r.MinimumTier != 0 || r.MaximumTier != 2 ||
            r.CostDestination is not null || r.UsesPerPhase is not null || r.UsesPerActualTurn is not null || r.UsageGroup is not null ||
            r.ActivationUsageGroup is not null || r.NameLedgerId is not null || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.SingleCardTrickUse || r.ExcludeOwnerEffects || r.DeclaredEntity || r.DeclarationValidation is not null || r.AllowChainedInput ||
            r.SameSuit || r.InheritPreviousPlaySuit || r.VariableInputCount || r.DistanceUnlimited || r.NoDying ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.UseEffectiveInputSuit is not null || r.KenshangUse == true ||
            r.SourcePileSkillId is not null || r.NonBasicUnrespondableSlash || r.LastInSourceZone is not null ||
            r.DisableSkillUntilTurnEndIfNoDamage is not null || r.AllHandCardsOfSelectedSuit)
            throw new InvalidOperationException(path + ": Leiluan requires only its dynamic exact HE/grain count and native use-only basic direction.");
    }
}

public sealed record LeiluanMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record LeiluanNativePayment(long Id, long UseId, CardConversionSource Source, string GameplayHash,
    int OwnerSeat, int ActualTurn, int TurnOwner, int Round, CardKind Kind, bool ResponseUse, int FrozenX, long Before)
{
    public long ActionId { get; init; }
    public long After { get; init; }
    public bool Paid { get; init; }
    public bool LastHand { get; init; }
    private readonly IReadOnlyList<LeiluanMaterial> _materials = Array.AsReadOnly(Array.Empty<LeiluanMaterial>());
    private readonly IReadOnlyList<int> _handBefore = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<LeiluanMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> HandBefore { get => _handBefore; init => _handBefore = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}

// New rule facts are scalar; private collections stay frozen on their real owning frames.
public sealed record LeiluanPaymentPreparedEvent(long PaymentId, long UseId, CardConversionSource Source, string GameplayHash, int OwnerSeat, int ActualTurn, int TurnOwner, int Round, CardKind Kind, bool ResponseUse, int X, int HandCount, long Before) : IGameEvent;
public sealed record LeiluanMaterialFrozenEvent(long PaymentId, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record LeiluanHandFrozenEvent(long PaymentId, int Index, int CardId) : IGameEvent;
public sealed record LeiluanMaterialPaidEvent(long PaymentId, int Index, int CardId, long Sequence, long BatchId) : IGameEvent;
public sealed record LeiluanPaymentBoundEvent(long PaymentId, long UseId, long ActionId, bool LastHand, long After) : IGameEvent;
public sealed record LeiluanBenefitIssuedEvent(long ProgramId, long PaymentId, long UseId, long ActionId, int OwnerSeat) : IGameEvent;
public sealed record LeiluanDisabledEvent(long ProgramId, int OwnerSeat, string SkillId, string StateId) : IGameEvent;
internal sealed class ResolveLeiluanLastHandUseBenefitDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit) {
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class LeiluanCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "ol:leiluan";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Window != SkillProgramTriggerWindow.CardUseCommitted || !t.IncludeResponseUses ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.ConversionSource || t.SourceSkillId != SkillId ||
            !t.AllowNoEventTarget || !t.SingleActionInstance || t.Optional || t.Subject is not null ||
            t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ResolveLeiluanLastHandUseBenefit, Target: SkillProgramEffectTarget.Owner }])
            throw new InvalidOperationException("Leiluan requires its exact original-payer committed basic-use benefit.");
    }
}
