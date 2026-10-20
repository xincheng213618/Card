using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouJiangWeiStage { Targets, Responding, GiftChildren, Issuing, NativeUse, NativeReturned, AwakeningChildren, MarkTargets, FactionRequest }
public sealed record MouJiangWeiPayment(int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence);
public sealed record MouJiangWeiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, ActualDiscardRecoveryPhaseKey? Phase)
{
    public MouJiangWeiStage Stage { get; init; }
    public bool Accepted { get; init; }
    public int Cursor { get; init; }
    public int Budget { get; init; }
    public int TurnOrdinal { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public LegalAction? NativeAction { get; init; }
    public MouJiangWeiPayment? Payment { get; init; }
    public FactionResponsePolicySource? FactionPolicy { get; init; }
    public bool FactionTried { get; init; }
    public int ProviderSeat { get; init; } = -1;
    private IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _materials = [];
    public IReadOnlyList<int> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouJiangWeiNativeOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Index, long UseId, long ActionId, int ActorSeat, int TargetSeat)
{ public int ProviderSeat { get; init; } }
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouJiangWeiReceipt? MouJiangWei { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouJiangWeiNativeOrigin? MouJiangWeiRequest { get; init; } }
public sealed record MouJiangWeiInitializedEvent(long FrameId, int OwnerSeat, CardConversionSource Source, int Charge) : IGameEvent;
public sealed record MouJiangWeiChargeChangedEvent(long FrameId, int OwnerSeat, long DiscardInvocationId, int Before, int After) : IGameEvent;
public sealed record MouJiangWeiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, ActualDiscardRecoveryPhaseKey? Phase) : IGameEvent;
public sealed record MouJiangWeiTargetAcceptedEvent(long FrameId, int OwnerSeat, int Index, int TargetSeat) : IGameEvent;
public sealed record MouJiangWeiSelectionClosedEvent(long FrameId, int OwnerSeat, int Count) : IGameEvent;
public sealed record MouJiangWeiGiftPaidEvent(long FrameId, int Index, int GiverSeat, MouJiangWeiPayment Payment) : IGameEvent;
public sealed record MouJiangWeiSlashIssuedEvent(MouJiangWeiNativeOrigin Origin) : IGameEvent;
public sealed record MouJiangWeiSlashReturnedEvent(long FrameId, int Index, long UseId, long ActionId) : IGameEvent;
public sealed record MouJiangWeiFactionRequestedEvent(long FrameId, int Index, int ActorSeat, int TargetSeat, FactionResponsePolicySource Policy) : IGameEvent;
public sealed record MouJiangWeiFactionProvidedEvent(long FrameId, int Index, int ActorSeat, int ProviderSeat, CardKind Kind, int MaterialCount) : IGameEvent;
public sealed record MouJiangWeiFactionDeclinedEvent(long FrameId, int Index, int ActorSeat) : IGameEvent;
public sealed record MouJiangWeiAwakenedEvent(long FrameId, int OwnerSeat, CardConversionSource Source, string GameplayHash, int SelectedCount) : IGameEvent;
public sealed record MouJiangWeiBeifaGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int HolderSeat, int TurnOrdinal) : IGameEvent;
public sealed record MouJiangWeiBeifaExpiredEvent(long FrameId, int HolderSeat, int TurnOrdinal) : IGameEvent;
public sealed record MouJiangWeiCompletedEvent(long FrameId, SkillProgramEffectOp Op, int SelectedCount) : IGameEvent;
internal interface IMouJiangWeiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouJiangWeiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.InitializeMouJiangWei ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard, Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
    public override ISkillProgramEffectHandler Handler => new MouJiangWeiHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    { SkillProgramEffectOp.InitializeMouJiangWei => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)], SkillProgramEffectOp.ResolveMouJiangWeiZhiji => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)], _ => [] };
}
internal sealed class InitializeMouJiangWeiDescriptor : MouJiangWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouJiangWei; }
internal sealed class ResolveMouJiangWeiTiaoxinDescriptor : MouJiangWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouJiangWeiTiaoxin; }
internal sealed class ResolveMouJiangWeiZhijiDescriptor : MouJiangWeiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouJiangWeiZhiji; }
internal sealed class MouJiangWeiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouJiangWeiHost>().Resolve(f, op); }
