using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouXuHuangShipoStage { ModeChoice, RecipientChoice, GiftChildren, Damage, TransferCards, TransferRecipient, TransferChildren }
public sealed record MouXuHuangShipoReceipt(CardConversionSource Source, string GameplayHash,
    long EndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int OwnerHp)
{
    public MouXuHuangShipoStage Stage { get; init; }
    public string? Mode { get; init; }
    public bool Accepted { get; init; }
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int? TransferRecipient { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>()), _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<MouXuHuangGiftReceipt> _gifts = Array.AsReadOnly(Array.Empty<MouXuHuangGiftReceipt>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedCardIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouXuHuangGiftReceipt> Gifts { get => _gifts; init => _gifts = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouXuHuangShipoStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long EndingWindowId, ActualDiscardRecoveryPhaseKey Phase, int OwnerHp) : IGameEvent;
public sealed record MouXuHuangShipoModeAcceptedEvent(long FrameId, string Mode, int Count) : IGameEvent;
public sealed record MouXuHuangShipoTargetFrozenEvent(long FrameId, int Index, int TargetSeat) : IGameEvent;
public sealed record MouXuHuangShipoGiftPaidEvent(long FrameId, int Cursor, int GiverSeat, int CardId, CardLocation To, long Sequence) : IGameEvent;
public sealed record MouXuHuangShipoDamageIssuedEvent(long FrameId, int Cursor, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record MouXuHuangShipoRecipientCompletedEvent(long FrameId, int Cursor, int TargetSeat, bool SkippedDead) : IGameEvent;
public sealed record MouXuHuangShipoTransferSelectedEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record MouXuHuangShipoTransferPaidEvent(long FrameId, int RecipientSeat, long BatchId, int Count, long Before, long After) : IGameEvent;
public sealed record MouXuHuangShipoCompletedEvent(long FrameId, int ActualObtainedCount, int TransferredCount) : IGameEvent;
internal sealed class ResolveMouXuHuangShipoDescriptor : MouXuHuangDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXuHuangShipo;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
