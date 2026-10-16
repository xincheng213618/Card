using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum EquipmentSlotNamedUseIntent { Play, Dodge, Nullification, DyingPeach, DyingAlcohol, BorrowedSword, Qinglong }
public enum EquipmentSlotNamedUseStage { ChoosingSlot, MovementChildren }
public sealed record EquipmentSlotNamedUsePayment(long FrameId, CardConversionSource Source, string GameplayHash,
    int RoundNumber, int ActualTurn, int ActualTurnOwner, int PhaseInstance, bool Yin, CardKind EffectiveKind,
    EquipmentSlot Slot, int PreviousCapacity, long Before, long After, EquipmentSlotNamedUseIntent Intent,
    PromptId OriginalPromptId, long OriginalRevision, long? OriginalParentId, string TargetSignature);
public sealed record EquipmentSlotNamedUseDraft
{
    private PendingDecision _originalDecision = new(DecisionKind.PlayCard, 0, "", [], []);
    private LegalAction? _playAction;
    private PromptChoice? _responseChoice;
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required EquipmentSlotNamedUseIntent Intent { get; init; }
    public required CardKind EffectiveKind { get; init; }
    public required bool Yin { get; init; }
    public required int RoundNumber { get; init; }
    public required int ActualTurn { get; init; }
    public required int ActualTurnOwner { get; init; }
    public required int PhaseInstance { get; init; }
    public required string TargetSignature { get; init; }
    public required string OriginalDecisionSignature { get; init; }
    public long? OriginalParentId { get; init; }
    public PendingDecision OriginalDecision { get => _originalDecision; init => _originalDecision = RequestedDeckBasicFrame.FreezeDecision(value); }
    public LegalAction? PlayAction { get => _playAction; init => _playAction = value is null ? null : value with
    {
        TargetSeats = Array.AsReadOnly(value.TargetSeats.ToArray()),
        AdditionalConversionSources = Array.AsReadOnly((value.AdditionalConversionSources ?? []).ToArray()),
        SelectableCardIds = Array.AsReadOnly(value.SelectableCardIds.ToArray()),
        SelectableTargetSeats = Array.AsReadOnly(value.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = value.SelectableCardTargets is null ? null : Array.AsReadOnly(value.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = value.SelectableTargetSequences is null ? null : Array.AsReadOnly(value.SelectableTargetSequences.Select(s => (IReadOnlyList<int>)Array.AsReadOnly(s.ToArray())).ToArray())
    }; }
    public PromptChoice? ResponseChoice { get => _responseChoice; init => _responseChoice = value is null ? null : PromptChoiceProjection.Freeze(value); }
    public EquipmentSlotNamedUseStage Stage { get; init; }
    public EquipmentSlotNamedUsePayment? Payment { get; init; }
}
public enum LuanfengStage { RecoveryChild, DrawChildren }
public sealed record LuanfengReceipt(long FrameId, CardConversionSource Source, string GameplayHash,
    long DyingFrameId, int VictimSeat, LuanfengStage Stage, int RecoveryAmount = 0, int RestoredSlots = 0, int RequestedDraw = 0,
    long Before = 0, long After = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EquipmentSlotNamedUseDraft? EquipmentSlotNamedUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LuanfengReceipt? Luanfeng { get; init; }
}
public sealed record EquipmentSlotNamedUseStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    EquipmentSlotNamedUseIntent Intent, CardKind EffectiveKind, bool Yin, int RoundNumber, PromptId OriginalPromptId,
    long OriginalRevision, long? OriginalParentId, string TargetSignature,
    int ActualTurn, int ActualTurnOwner, int PhaseInstance) : IGameEvent
{
    // A private response menu can reveal hand membership even through a digest oracle.
    // Keep this exact integrity proof in trusted replay state, outside public projections.
    [JsonIgnore] internal string OriginalDecisionSignature { get; init; } = "";
    public override string ToString() => $"Equipment-slot use {FrameId}, owner {Source.OwnerSeat}, output {EffectiveKind}, round {RoundNumber}";
}
public sealed record EquipmentSlotNamedUsePaidEvent(EquipmentSlotNamedUsePayment Payment) : IGameEvent;
public sealed record EquipmentSlotNamedUseConsumedEvent(long PaymentFrameId) : IGameEvent;
public sealed record EquipmentSlotNamedUseIssuedEvent(long PaymentFrameId, long NativeOwnerId, long CardActionId) : IGameEvent;
public sealed record EquipmentSlotNamedUseCancelledEvent(long PaymentFrameId) : IGameEvent;
public sealed record EquipmentSlotNamedNamesResetEvent(int OwnerSeat, long LuanfengFrameId) : IGameEvent;
public sealed record LuanfengIssuedEvent(long FrameId, CardConversionSource Source, long DyingFrameId, int VictimSeat) : IGameEvent;
public sealed record LuanfengSlotsRestoredEvent(long FrameId, int VictimSeat, int RestoredSlots, int RequestedDraw) : IGameEvent;

internal interface IWoLongFengChuProgramHost
{ SkillProgramStepOutcome ResolveLuanfeng(ProgramSkillFrame frame); }
internal abstract class WoLongFengChuOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ISkillProgramEffectHandler Handler => new WoLongFengChuOperationHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, _) => { });
}
internal sealed class PayEquipmentSlotForNamedUseDescriptor : WoLongFengChuOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PayEquipmentSlotForNamedUse; }
internal sealed class ResolveLuanfengDyingRecoveryDescriptor : WoLongFengChuOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLuanfengDyingRecovery;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, context) => context.PublicControlValue(12));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}
internal sealed class WoLongFengChuOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveLuanfengDyingRecovery ? host.GetCapability<IWoLongFengChuProgramHost>().ResolveLuanfeng(frame) :
            throw new InvalidOperationException("An equipment-slot conversion payment requires its exact native producer and owning cost frame.");
}
