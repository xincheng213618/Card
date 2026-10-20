using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramXieweiBaitBasicUsePolicy(string PileSkillId);
internal static class XieweiBaitBasicUseSchema
{
    internal static ProgramXieweiBaitBasicUsePolicy Parse(JsonElement node, string path)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 1 ||
            !node.TryGetProperty("pileSkillId", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != "ol:xiewei")
            throw new InvalidOperationException(path + ": Xiewei requires its exact bait-pile skill.");
        return new("ol:xiewei");
    }
    internal static void Validate(SkillProgramViewAs r, string path)
    {
        if (r.XieweiBaitBasicUse is null) return;
        var dodge = r.OutputKind == CardKind.Dodge;
        if (r.XieweiBaitBasicUse.PileSkillId != "ol:xiewei" || r.OutputKind is not (CardKind.Slash or CardKind.Dodge) ||
            r.InputCount != 2 || r.VariableInputCount || !r.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            r.InputKinds.Count != 0 || r.InputSuits.Count != 0 || r.InputCategories.Count != 0 ||
            !r.UseOnly || !r.ExtendedUse || !r.AllowSameKind || r.ForPlay == dodge || r.ForResponse != dodge ||
            r.Condition.Kind != SkillProgramConditionKind.Always || r.LeiluanBasicUse is not null || r.TieredRoundConversion is not null ||
            r.RoundDistinctBasicUse is not null || r.RoundOnceCardlessBasic is not null || r.DrawFundedDistinctBasic is not null ||
            r.ChainedStateCost is not null || r.RoundGainedRedHandOnly is not null || r.SourcePileSkillId is not null ||
            r.ConversionStateId is not null || r.CostDestination is not null || r.UsesPerPhase is not null || r.UsesPerActualTurn is not null ||
            r.UsageGroup is not null || r.SameSuit || r.AllHandCardsOfSelectedSuit || r.AllowChainedInput || r.DistanceUnlimited ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.LastInSourceZone is not null || r.NoDying || r.DeclaredEntity ||
            r.DeclarationValidation is not null || r.SingleCardTrickUse || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.NameLedgerId is not null || r.DisableSkillUntilTurnEndIfNoDamage is not null || r.InheritPreviousPlaySuit ||
            r.NonBasicUnrespondableSlash || r.UseEffectiveInputSuit is not null || r.MinimumTier != 0 || r.MaximumTier != 2 ||
            r.ActivationUsageGroup is not null || r.ExcludeOwnerEffects)
            throw new InvalidOperationException(path + ": Xiewei requires exactly two Hand costs, an ordinary true Slash/Dodge and its original public-pile payment.");
    }
}
public sealed record XieweiNativePayment(long Id, long UseId, CardConversionSource Source, string GameplayHash,
    int OwnerSeat, int ActorSeat, int? RequesterSeat, int ActualTurn, int TurnOwner, bool ResponseUse,
    CardKind Kind, PublicPersistentPileSource Pile, long Before)
{
    public long ActionId { get; init; }
    public long After { get; init; }
    public bool Paid { get; init; }
    private IReadOnlyList<GuanHaiMaterial> _materials = Array.AsReadOnly(Array.Empty<GuanHaiMaterial>());
    private IReadOnlyList<CardMovementRecord> _movements = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<GuanHaiMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
}

public sealed record XieweiPaymentPreparedEvent(long PaymentId, long UseId, CardConversionSource Source, string GameplayHash, int OwnerSeat, int ActorSeat, int? RequesterSeat, int ActualTurn, int TurnOwner, bool ResponseUse, CardKind Kind, PublicPersistentPileSource Pile, long Before) : IGameEvent;
public sealed record XieweiMaterialFrozenEvent(long PaymentId, int Index, int CardId, CardKind Kind, CardLocation From) : IGameEvent;
public sealed record XieweiBaitStoredEvent(long PaymentId, int Index, int CardId, CardLocation From, CardLocation Pile, long Sequence, long BatchId) : IGameEvent;
public sealed record XieweiOutputBoundEvent(long PaymentId, long UseId, long ActionId, int ActorSeat, int ProviderSeat, int? RequesterSeat, CardKind Kind, bool ResponseUse) : IGameEvent;
public sealed record XieweiCommittedEvent(long ProgramId, long PaymentId, long UseId, long ActionId) : IGameEvent;
public sealed record XieweiTargetFrozenEvent(long UseId, long ActionId, int Index, int TargetSeat, int Maximum) : IGameEvent;
internal sealed class ResolveXieweiTwoHandBaitBasicUseDescriptor() : GuanHaiDescriptor(SkillProgramEffectOp.ResolveXieweiTwoHandBaitBasicUse){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.GuanHai is { Paid: true } || context.State.CanContinueAfterOwnerDeath(context.Frame);
}
