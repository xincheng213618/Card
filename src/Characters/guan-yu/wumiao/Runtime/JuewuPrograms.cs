using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record JuewuUseReceipt(long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner, CardKind OutputKind, CardKind NativeKind,
    int CardId, CardKind PrintedKind, int PrintedRank, CardLocation From, long SequenceBefore,
    long? ForeignHandEntrySequence)
{
    public long PaymentSequence { get; init; }
    public long PaymentBatchId { get; init; }
    private readonly IReadOnlyList<JuewuPaymentGrain> _grains = Array.AsReadOnly(Array.Empty<JuewuPaymentGrain>());
    public IReadOnlyList<JuewuPaymentGrain> Grains
    { get => _grains; init => _grains = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardUsePaymentMovementReceipt> _paymentBatches = Array.AsReadOnly(Array.Empty<CardUsePaymentMovementReceipt>());
    public IReadOnlyList<CardUsePaymentMovementReceipt> PaymentBatches
    { get => _paymentBatches; init => _paymentBatches = Array.AsReadOnly(value.Select(receipt => receipt with { Batch = receipt.Batch }).ToArray()); }
}
public sealed record JuewuPaymentGrain(int CardId, CardKind PrintedKind, CardLocation From);

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JuewuUseReceipt? Juewu { get; init; }
}

public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? JuewuEffectiveRank { get; init; }
}

public sealed record JuewuUseIssuedEvent(long UseId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner, CardKind OutputKind, CardKind NativeKind,
    int CardId, CardKind PrintedKind, int PrintedRank, CardLocation From, long SequenceBefore,
    long? ForeignHandEntrySequence) : IGameEvent;
public sealed record JuewuMaterialPaidEvent(long UseId, long ActionId, int CardId, CardLocation From,
    CardLocation To, CardMoveReason Reason, long MovementSequence) : IGameEvent;
public sealed record JuewuUseFinishedEvent(long UseId, long ActionId, CardConversionSource Source,
    CardKind OutputKind, int CardId, long PaymentSequence) : IGameEvent;
public sealed record JuewuPaymentBatchIssuedEvent(long UseId, long ActionId, long BatchId,
    long? ParentBatchId, int MaterialCount, int SequenceBefore, int SequenceAfter, bool Primary) : IGameEvent;

internal static class JuewuComposition
{
    internal static readonly IReadOnlyList<CardKind> Outputs = Array.AsReadOnly(new[]
    {
        CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
        CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack, CardKind.WumiaoFlood
    });

    internal static bool IsSlash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    internal static string Binding(CardKind kind)
    {
        var text = kind.ToString();
        return "rank-two-" + char.ToLowerInvariant(text[0]) + text[1..];
    }

    [ProgramCatalogValidation(1200)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (!program.ViewAs.Any(rule => rule.RankTwoDamageUse) && program.Id != "wumiao:juewu") return;
        if (program.Id != "wumiao:juewu" || program.ViewAs.Count != Outputs.Count ||
            !program.ViewAs.Select(rule => rule.OutputKind).ToHashSet().SetEquals(Outputs) ||
            program.Activations.Count != 0 || program.Triggers.Count != 0 || program.Modifiers.Count != 0 ||
            program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
            program.DamageModifiers.Count != 0 || program.BooleanStates.Count != 0 || program.Contributions.Count != 0 ||
            program.ViewAs.Any(rule => !Exact(rule)))
            throw new InvalidOperationException(path + ": Juewu requires its complete one-entity rank-two damage-card use family and independent actual-turn name allowances.");
    }

    private static bool Exact(SkillProgramViewAs rule) =>
        rule.RankTwoDamageUse && rule.Id == Binding(rule.OutputKind) && rule.InputCount == 1 &&
        !rule.VariableInputCount && rule.InputKinds.Count == 0 && rule.InputSuits.Count == 0 &&
        rule.InputCategories.Count == 0 && rule.SourceZones.SequenceEqual(new[]
            { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.WoodenOxGrain }) &&
        rule.UseOnly && rule.AllowSameKind && rule.UsesPerActualTurn == 1 && rule.UsesPerPhase is null &&
        rule.UsageGroup is null && rule.ActivationUsageGroup is null && rule.ForPlay &&
        rule.ForResponse == IsSlash(rule.OutputKind) && rule.SingleCardTrickUse == !IsSlash(rule.OutputKind) &&
        !rule.ExtendedUse && !rule.EqualNameLengthUse && !rule.DistanceUnlimited &&
        rule.Condition.Kind == SkillProgramConditionKind.Always && rule.DeclarationValidation is null &&
        rule.CostDestination is null && rule.ConversionStateId is null && !rule.DeclaredEntity &&
        rule.TieredRoundConversion is null && rule.RoundOnceCardlessBasic is null && rule.RoundDistinctBasicUse is null &&
        rule.DrawFundedDistinctBasic is null && rule.ChainedStateCost is null && rule.LeiluanBasicUse is null &&
        rule.XieweiBaitBasicUse is null && rule.KenshangUse != true && rule.SourcePileSkillId is null &&
        rule.RoundGainedRedHandOnly is null && rule.LastInSourceZone is null && !rule.SameSuit &&
        !rule.AllHandCardsOfSelectedSuit && !rule.AllowChainedInput && !rule.InheritPreviousPlaySuit &&
        !rule.NoDying && !rule.ExcludeOwnerEffects && !rule.UnusedOutputThisTurn && !rule.UnusedOutputNameThisGame &&
        rule.NameLedgerId is null && rule.DisableSkillUntilTurnEndIfNoDamage is null && !rule.NonBasicUnrespondableSlash &&
        rule.UseEffectiveInputSuit is null && rule.DamageBonus == 0 && rule.RecoveryBonus == 0 &&
        rule.MinimumTier == 0 && rule.MaximumTier == 2;

    internal static void ValidateViewAs(string path, SkillProgramViewAs rule)
    {
        if (rule.RankTwoDamageUse && (!Outputs.Contains(rule.OutputKind) || !Exact(rule)))
            throw new InvalidOperationException(path + ": rankTwoDamageUse requires one original rank-two HE/Ox entity, true Use, and its independent actual-turn output allowance.");
    }
}
