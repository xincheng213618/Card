using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SiruiNativeUseKind { CardUse, Dodge, Nullification }

// A single original native owner retains the invoice, including while its
// ordinary payment observers, damage and rescue children are running.
public sealed record SiruiUseReceipt(long OwnerFrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, SiruiNativeUseKind NativeKind, CardKind OutputKind,
    int CardId, CardKind PrintedKind, CardLocation From, long SequenceBefore)
{
    public long ActionId { get; init; }
    public long PaymentSequence { get; init; }
}

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SiruiUseReceipt? Sirui { get; init; }
}

public sealed record SiruiPhaseUseIssuedEvent(long OwnerFrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, SiruiNativeUseKind NativeKind, CardKind OutputKind,
    int CardId, CardKind PrintedKind, CardLocation From, long SequenceBefore) : IGameEvent;
public sealed record SiruiMaterialPaidEvent(long OwnerFrameId, int CardId, CardLocation From,
    CardLocation To, CardMoveReason Reason, long MovementSequence) : IGameEvent;
public sealed record SiruiNativeActionBoundEvent(long OwnerFrameId, long ActionId, int ActorSeat,
    int ProviderSeat, CardActionType ActionType, CardKind OutputKind) : IGameEvent;

internal static class SiruiComposition
{
    internal static readonly IReadOnlyList<CardKind> Outputs = Array.AsReadOnly(new[]
    {
        CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol,
        CardKind.DrawTwo, CardKind.Duel, CardKind.BarbarianAssault, CardKind.ArrowBarrage,
        CardKind.PeachGarden, CardKind.FiveGrains, CardKind.Dismantlement, CardKind.Snatch,
        CardKind.FireAttack, CardKind.IronChain, CardKind.BorrowedSword, CardKind.Nullification
    });

    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.ViewAs.Any(r => r.EqualNameLengthUse)) return;
        if (p.Id != "le:sirui" || p.ViewAs.Count != Outputs.Count ||
            !p.ViewAs.Select(r => r.OutputKind).ToHashSet().SetEquals(Outputs) ||
            p.Activations.Count != 0 || p.Triggers.Count != 0 || p.Modifiers.Count != 0 ||
            p.CardIdentities.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.ViewAs.Any(r => !Exact(r)))
            throw new InvalidOperationException(path + ": Sirui requires its complete equal-name, single HE true-use family and one shared actual Play allowance.");
    }

    private static bool Exact(SkillProgramViewAs r)
    {
        var responseOnly = r.OutputKind is CardKind.Dodge or CardKind.Nullification;
        var basic = r.OutputKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
            CardKind.Dodge or CardKind.Peach or CardKind.Alcohol;
        return r.EqualNameLengthUse && r.InputCount == 1 && !r.VariableInputCount &&
            r.InputKinds.Count == 0 && r.InputSuits.Count == 0 && r.InputCategories.Count == 0 &&
            r.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) &&
            r.UseOnly && r.AllowSameKind && r.UsesPerPhase == 1 && r.UsageGroup == "sirui" &&
            r.ForPlay == !responseOnly && r.ForResponse == (basic || responseOnly) &&
            r.SingleCardTrickUse == (!basic && !responseOnly) &&
            r.Condition.Kind == SkillProgramConditionKind.Always && !r.ExtendedUse &&
            r.UsesPerActualTurn is null && r.ActivationUsageGroup is null && r.ConversionStateId is null &&
            r.CostDestination is null && r.DeclarationValidation is null && !r.DeclaredEntity &&
            r.TieredRoundConversion is null && r.RoundOnceCardlessBasic is null && r.RoundDistinctBasicUse is null &&
            r.DrawFundedDistinctBasic is null && r.ChainedStateCost is null && r.LeiluanBasicUse is null &&
            r.XieweiBaitBasicUse is null && r.KenshangUse != true && r.SourcePileSkillId is null &&
            r.RoundGainedRedHandOnly is null && r.LastInSourceZone is null && !r.SameSuit &&
            !r.AllHandCardsOfSelectedSuit && !r.AllowChainedInput && !r.DistanceUnlimited &&
            !r.InheritPreviousPlaySuit && !r.NoDying && !r.ExcludeOwnerEffects &&
            !r.UnusedOutputThisTurn && !r.UnusedOutputNameThisGame && r.NameLedgerId is null &&
            r.DisableSkillUntilTurnEndIfNoDamage is null && !r.NonBasicUnrespondableSlash &&
            r.UseEffectiveInputSuit is null && r.DamageBonus == 0 && r.RecoveryBonus == 0 &&
            r.MinimumTier == 0 && r.MaximumTier == 2;
    }
}
