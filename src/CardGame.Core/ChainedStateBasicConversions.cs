using System.Text.Json.Serialization;

namespace CardGame.Core;

// The nullable destination is an additive view-as cost. Omission keeps every
// existing material, response and usage contract unchanged.
internal static class ChainedStateBasicSchema
{
    internal static void Validate(SkillProgramViewAs rule, string path)
    {
        if (rule.ChainedStateCost is not { } destination) return;
        var dodge = rule.OutputKind == CardKind.Dodge;
        if (destination != dodge || !dodge && rule.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
            rule.InputCount != 0 || rule.SourceZones.Count != 0 || rule.InputKinds.Count != 0 || rule.InputSuits.Count != 0 || rule.InputCategories.Count != 0 ||
            !rule.UseOnly || rule.ForPlay == dodge || rule.ForResponse != dodge || rule.Condition.Kind != SkillProgramConditionKind.Always ||
            rule.TieredRoundConversion is not null || rule.DrawFundedDistinctBasic is not null || rule.RoundDistinctBasicUse is not null ||
            rule.LastInSourceZone is not null || rule.ConversionStateId is not null || rule.MinimumTier != 0 || rule.MaximumTier != 2 ||
            rule.CostDestination is not null || rule.UsesPerPhase is not null || rule.UsageGroup is not null || rule.ActivationUsageGroup is not null ||
            rule.UnusedOutputThisTurn || rule.UnusedOutputNameThisGame || rule.NameLedgerId is not null || rule.ExtendedUse || rule.SingleCardTrickUse ||
            rule.ExcludeOwnerEffects || rule.DeclaredEntity || rule.DeclarationValidation is not null || rule.AllowChainedInput || rule.SameSuit ||
            rule.InheritPreviousPlaySuit || rule.VariableInputCount || rule.DistanceUnlimited || rule.NoDying || rule.AllowSameKind ||
            rule.DamageBonus != 0 || rule.RecoveryBonus != 0 || rule.UseEffectiveInputSuit is not null || rule.DisableSkillUntilTurnEndIfNoDamage is not null)
            throw new InvalidOperationException(path + ": chained-state cost requires a genuine zero-material, use-only Dodge entry or Slash release without another cost, condition or usage policy.");
    }
}

public enum ChainedStateBasicIntent
{
    Play = 0, OwnSlashDodge = 1, BorrowedSword = 2, Qinglong = 3,
    ProgramSlash = 4, ProgramNearestSlash = 5, AssistedSlash = 6, NearestLegalSlash = 7
}
public enum ChainedStateBasicStage { ChangingState = 0, StateChildren = 1 }

// All fields are scalar, including the exact original native need. A later
// state change or source loss does not repay or revoke this accepted cost.
public sealed record ChainedStateBasicPayment(long PaymentFrameId, CardConversionSource Source, string GameplayHash,
    bool WasChained, bool DesiredChained, int ActualTurnNumber, int ActualTurnOwnerSeat,
    ChainedStateBasicIntent Intent, CardKind EffectiveKind, long? ParentFrameId, long? RequestFrameId,
    long? ParentActionId, int? TargetSeat, int Cursor, PromptId OriginalPromptId, long OriginalRevision,
    long? CharacterStateChangeId = null);
public sealed record ChainedStateBasicUseReceipt(ChainedStateBasicPayment Payment, long OwnerFrameId, long CardActionId);

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicUseReceipt? ChainedStateBasicUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicUseReceipt? ChainedStateBasicResponse { get; init; }
}

public sealed record ChainedStateBasicFrame : ResolutionFrame
{
    private PendingDecision _originalDecision = new(DecisionKind.PlayCard, 0, "", [], []);
    private IReadOnlyList<int> _targetSeats = Array.AsReadOnly(Array.Empty<int>());
    [JsonConstructor]
    public ChainedStateBasicFrame(long id, ChainedStateBasicPayment payment, PendingDecision originalDecision,
        IReadOnlyList<int> targetSeats, ChainedStateBasicStage stage = ChainedStateBasicStage.ChangingState,
        ResolutionFrameStep step = ResolutionFrameStep.ResolvingEffect) : base(id, ResolutionFrameKind.ChainedStateBasic, step)
    { Payment = payment; OriginalDecision = originalDecision; TargetSeats = targetSeats; Stage = stage; }
    public ChainedStateBasicPayment Payment { get; init; }
    public PendingDecision OriginalDecision { get => _originalDecision; init => _originalDecision = RequestedDeckBasicFrame.FreezeDecision(value); }
    public IReadOnlyList<int> TargetSeats { get => _targetSeats; init => _targetSeats = Array.AsReadOnly(value.ToArray()); }
    public ChainedStateBasicStage Stage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ActiveChildFrameId { get; init; }
}

public sealed record ChainedStateBasicStartedEvent(ChainedStateBasicPayment Payment) : IGameEvent;
public sealed record ChainedStateBasicPaidEvent(ChainedStateBasicPayment Payment) : IGameEvent;
public sealed record ChainedStateBasicIssuedEvent(ChainedStateBasicUseReceipt Receipt) : IGameEvent;
public sealed record ChainedStateBasicReturnedEvent(ChainedStateBasicUseReceipt Receipt) : IGameEvent;
public sealed record ChainedStateBasicCancelledEvent(ChainedStateBasicPayment Payment) : IGameEvent;
