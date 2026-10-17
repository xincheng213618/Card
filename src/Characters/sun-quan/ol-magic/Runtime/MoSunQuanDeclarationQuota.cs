using System.Text.Json.Serialization;

namespace CardGame.Core;

// No private printed face is copied into this permission or its public-facing
// facts. The native declaration retains the original CardActionCost itself.
public sealed record MoQuanyuDeclarationQuota(long DeclarationId, int ActorSeat, int ProviderSeat,
    int CardId, CardLocation From, CardConversionSource DeclarationSource,
    CardKind DeclaredKind, CardKind OutputKind, int TargetSeat, int ActualTurn, int TurnOwner,
    long SequenceBefore, bool Eligible, CardConversionSource? Source, string? GameplayHash,
    int ChoiceMask, bool Entered);

public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MoQuanyuDeclarationQuota? MoQuanyuDeclarationQuota { get; init; }
}

public sealed record MoQuanyuDeclarationQuotaCapturedEvent(long DeclarationId, long OwnerFrameId,
    MoQuanyuDeclarationQuota Receipt) : IGameEvent;
public sealed record MoQuanyuDeclarationPaymentRecordedEvent(long DeclarationId, long MovementSequence) : IGameEvent;
public sealed record MoQuanyuDeclarationQuotaBoundEvent(long DeclarationId, long UseId, long ActionId,
    int ActorSeat, int TargetSeat, CardKind OutputKind, bool Eligible) : IGameEvent;
