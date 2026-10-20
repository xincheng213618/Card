using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouGuoYouZhiStage { GiftChoice, GiftChildren, AdjustChoice, AdjustDiscard, Drawing, DrawChildren, DiscardChildren, DiscardChoice, ObtainChildren, UseChoice, IssuingUse, NativeUse, Returned }
public sealed record MouGuoYouZhiGift(int CardId, CardKind EffectiveKind);
public sealed record MouGuoYouZhiNativeReceipt(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int ActorSeat, int CardId, int ActualTurn, int TurnOwner, long ActualPhase,
    LegalAction Action, long UseId, long ActionId);
public sealed record MouGuoYouZhiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    int ActorSeat, int ActualTurn, long ActualPhase)
{
    public MouGuoYouZhiStage Stage { get; init; }
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int DistinctNames { get; init; }
    public int CardId { get; init; }
    public int Requested { get; init; }
    private IReadOnlyList<int> _watched = [], _selected = [];
    private IReadOnlyList<MouGuoYouZhiGift> _gifts = [];
    public IReadOnlyList<int> WatchedHand { get => _watched; init => _watched = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouGuoYouZhiGift> Gifts { get => _gifts; init => _gifts = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
    public MouGuoYouZhiNativeReceipt? Native { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGuoYouZhiReceipt? MouGuoYouZhi { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGuoYouZhiNativeReceipt? MouGuoYouZhiUse { get; init; } }
public sealed record MouGuoYouZhiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActorSeat, int ActualTurn, long ActualPhase) : IGameEvent;
public sealed record MouGuoYouZhiViewedEvent(long FrameId, int OwnerSeat, int TargetSeat, int Count) : IGameEvent;
public sealed record MouGuoYouZhiGiftAcceptedEvent(long FrameId, int TargetSeat, int Count) : IGameEvent;
public sealed record MouGuoYouZhiGiftReturnedEvent(long FrameId, int ActualCount, int DistinctNames) : IGameEvent;
public sealed record MouGuoYouZhiAdjustChosenEvent(long FrameId, int DesiredCount, int CurrentCount, bool Accepted) : IGameEvent;
public sealed record MouGuoYouZhiObtainIssuedEvent(long FrameId, int ActorSeat, int CardId) : IGameEvent;
public sealed record MouGuoYouZhiMovementEvent(long FrameId, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, CardMoveReason Reason, int Sequence) : IGameEvent;
public sealed record MouGuoYouZhiDrawIssuedEvent(long FrameId, int OwnerSeat, int Requested) : IGameEvent;
public sealed record MouGuoYouZhiDrawReturnedEvent(long FrameId, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouGuoYouZhiNativeIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, int CardId) : IGameEvent;
public sealed record MouGuoYouZhiNativeReturnedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, int CardId) : IGameEvent;
public sealed record MouGuoYouZhiCompletedEvent(long FrameId, SkillProgramEffectOp Op, int DistinctNames, long? NativeUseId) : IGameEvent;

internal interface IMouGuoYouZhiHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class MouGuoYouZhiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new MouGuoYouZhiHandler(Op);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class ResolveMouGuoYouZhiBiqueDescriptor : MouGuoYouZhiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuoYouZhiBique;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class ResolveMouGuoYouZhiZhongchunDescriptor : MouGuoYouZhiDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuoYouZhiZhongchun;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class MouGuoYouZhiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) => h.GetCapability<IMouGuoYouZhiHost>().Resolve(f, op);
}
