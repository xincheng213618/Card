namespace CardGame.Core;

internal interface IBianFuRenProgramHost
{
    void ResolveBianFuRenMovement(ProgramSkillFrame frame, SkillProgramEffectOp operation);
}

internal abstract class BianFuRenMovementDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    // The character validates its frozen multi-zone batch invoice at runtime;
    // the generic single-source static context does not model that receipt.
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.None;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(
        ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(
            new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override ISkillProgramEffectHandler Handler => new BianFuRenMovementHandler(Op);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.WanweiObtainMatchingLostCard
            ? SkillProgramTriggerWindow.CardsMoved : SkillProgramTriggerWindow.DiscardPileReceived)];
}

internal sealed class BianFuRenMovementHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.GetCapability<IBianFuRenProgramHost>().ResolveBianFuRenMovement(frame, operation);
        // The next ordinary awaitOwnedCardMovement instruction owns every child
        // return. This operation has no second payment path or pending sidecar.
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed record BianFuRenOwnedLossReceipt(long BatchId, long ProducerFrameId, int OwnerSeat,
    int ActorSeat, int CardId, CardKind PrintedKind, CardLocation OriginalFrom,
    int EntryMovementSequence, int CompletionMovementSequence, bool Discarded);

public sealed record BianFuRenUsePayment(long NativeUseId, long ActionId, int CardId,
    CardKind PrintedKind, CardLocation OriginalFrom, int MovementSequence);

public sealed partial record CardUseFrame
{
    private readonly IReadOnlyList<BianFuRenUsePayment>? _bianFuRenUsePayments;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BianFuRenUsePayment>? BianFuRenUsePayments
    {
        get => _bianFuRenUsePayments;
        init => _bianFuRenUsePayments = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed partial record DeclaredCardPayment
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? BianFuRenPaymentSequence { get; init; }
}

public sealed record BianFuRenUsedMaterial(int CardId, CardKind PrintedKind, int PaymentMovementSequence,
    int CompletionMovementSequence, int DelayedPlacementMovementSequence = 0,
    BianFuRenUsePayment? OriginalPayment = null);

public sealed record BianFuRenUsedDiscardReceipt
{
    public required long BatchId { get; init; }
    public required long NativeUseId { get; init; }
    public required CardActionContext Action { get; init; }
    private readonly IReadOnlyList<BianFuRenUsedMaterial> _materials = [];
    public required IReadOnlyList<BianFuRenUsedMaterial> Materials
    {
        get => _materials;
        init => _materials = Array.AsReadOnly(value.ToArray());
    }
}

public sealed partial record CardMovementBatchContext
{
    private readonly IReadOnlyList<BianFuRenOwnedLossReceipt>? _bianFuRenOwnedLosses;
    private readonly IReadOnlyList<BianFuRenUsedDiscardReceipt>? _bianFuRenUsedDiscards;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BianFuRenOwnedLossReceipt>? BianFuRenOwnedLosses
    {
        get => _bianFuRenOwnedLosses;
        init => _bianFuRenOwnedLosses = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BianFuRenUsedDiscardReceipt>? BianFuRenUsedDiscards
    {
        get => _bianFuRenUsedDiscards;
        init => _bianFuRenUsedDiscards = value is null ? null : Array.AsReadOnly(value.Select(item =>
            item with { Materials = item.Materials }).ToArray());
    }
}

/// <summary>Scalar producer proof; the movement ledger retains private material identities.</summary>
public sealed record BianFuRenForeignDiscardProducerEvent(int MovementSequence, int OriginalEntrySequence,
    long ProducerFrameId, int ActorSeat, int CardOwnerSeat) : IGameEvent;

public sealed record BianFuRenOwnedTransferProducerEvent(int EntryMovementSequence, long ProducerId,
    int CardOwnerSeat, int RecipientSeat) : IGameEvent;

// A delayed card leaves its native use frame before its eventual disposition.
// This scalar invoice freezes that exact live use and physical placement; the
// accepted action and ledger supply the immutable target/material details.
public sealed record BianFuRenDelayedUsePlacedEvent(long NativeUseId, long ActionId, int CardId,
    int PaymentMovementSequence, int PlacementMovementSequence, int ActorSeat, int OriginalTargetSeat,
    CardKind EffectiveKind, Suit? EffectiveSuit) : IGameEvent;
