using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record OrdinaryQietingTurnContext(int ActualTurn, int TurnOwnerSeat, long HistoryCutoff, bool UsedOnAnother);
public sealed record OrdinaryQietingActualUseEvent(int ActualTurn, int TurnOwnerSeat, long NativeUseId,
    long? ActionId, int ActorSeat, bool UsedOnAnother, int DesignatedTargetCount) : IGameEvent;
public enum OrdinaryQietingStage { Choice, MovementChildren, Draw, DrawChildren, Complete }
public sealed record OrdinaryQietingReceipt(CardConversionSource Source, string GameplayHash,
    OrdinaryQietingTurnContext Qualification, long WindowId, int OccurrenceIndex, OrdinaryQietingStage Stage)
{
    public bool Paid { get; init; }
    public string? Option { get; init; }
    public int? CardId { get; init; }
    public CardKind? PrintedKind { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _grainIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> GrainIds { get => _grainIds; init => _grainIds = Array.AsReadOnly(value.ToArray()); }
}
public sealed record OrdinaryQietingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    OrdinaryQietingTurnContext Qualification, long WindowId, int OccurrenceIndex) : IGameEvent;
public sealed record OrdinaryQietingOptionIssuedEvent(long FrameId, string Option, int? CardId, CardKind? PrintedKind, long Before) : IGameEvent;
public sealed record OrdinaryQietingEquipmentMovedEvent(long FrameId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record OrdinaryQietingDrawReturnedEvent(long FrameId, int ActualCount, int ReplacementCount,
    long Before, long After, long? AttemptId) : IGameEvent;
public sealed record OrdinaryQietingCompletedEvent(long FrameId, bool Paid) : IGameEvent;
internal sealed class ResolveOrdinaryCaiFuRenQietingDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new OrdinaryCaiFuRenQietingHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class OrdinaryCaiFuRenQietingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveOrdinaryCaiFuRenQieting;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IOrdinaryCaiFuRenHost>().Qieting(f);
}
