using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouDengAiOlStage { ExchangeHand, ExchangeGu, Cost, RemoveGu, CostChildren, Material, Snatch, Issuing, NativeUse, Children }
public sealed record MouDengAiOlReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, MouDengAiOlStage Stage)
{
    public long WindowId { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int CardId { get; init; }
    public int Distance { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Paid { get; init; }
    public bool NativeReturned { get; init; }
    private IReadOnlyList<int> _hand = Array.AsReadOnly(Array.Empty<int>()), _gu = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> HandCards { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> GuCards { get => _gu; init => _gu = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<long> _batches = Array.AsReadOnly(Array.Empty<long>());
    public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouDengAiSnatchOrigin(long FrameId, CardConversionSource Source, string GameplayHash, long UseId, long ActionId, int CardId, int Distance);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDengAiOlReceipt? MouDengAiOl { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDengAiSnatchOrigin? MouDengAiJiewan { get; init; } }
public sealed record MouDengAiUseEntryEvent(long ParentId, long ParentActionId, int CardId, long EntrySequence, int ActorSeat, bool OutsideOwnPlay) : IGameEvent;
public sealed record MouDengAiUseDiscardEvent(long BatchId, int CardId, long Sequence, long ParentId, int ActorSeat) : IGameEvent;
public sealed record MouDengAiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, long WindowId) : IGameEvent;
public sealed record MouDengAiGuCollectedEvent(long FrameId, int CardId, long DiscardSequence, long ClaimSequence, int CountBefore, int MaximumHp) : IGameEvent;
public sealed record MouDengAiExchangeEvent(long FrameId, int EnteringCardId, int LeavingCardId, CardLocation PileLocation) : IGameEvent;
public sealed record MouDengAiCostPaidEvent(long FrameId, string Cost, int CardId, long Sequence, int MaximumHp) : IGameEvent;
public sealed record MouDengAiDistanceFrozenEvent(long FrameId, int GuCount, int Distance) : IGameEvent;
public sealed record MouDengAiSnatchIssuedEvent(MouDengAiSnatchOrigin Origin) : IGameEvent;
public sealed record MouDengAiSnatchReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouDengAiNativeBatchEvent(long FrameId, long UseId, long BatchId, long? ParentBatchId, int Count) : IGameEvent;
public sealed record MouDengAiGrownEvent(long FrameId, int PreviousMaximumHp, int MaximumHp) : IGameEvent;
public sealed record MouDengAiCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;
internal interface IMouDengAiOlHost { SkillProgramStepOutcome Start(ProgramSkillFrame f, SkillProgramEffectOp operation); }
internal abstract class MouDengAiOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouDengAiOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry | ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(Op switch
    {
        SkillProgramEffectOp.CollectMouDengAiJigu => SkillProgramTriggerWindow.DiscardPileReceived,
        SkillProgramEffectOp.GrowMouDengAiJiewan => SkillProgramTriggerWindow.TurnEnding,
        _ => SkillProgramTriggerWindow.TurnStartBeforeNormalFlow
    })];
}
internal sealed class CollectMouDengAiJiguDescriptor : MouDengAiOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectMouDengAiJigu; }
internal sealed class ExchangeMouDengAiJiguDescriptor : MouDengAiOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeMouDengAiJigu; }
internal sealed class UseMouDengAiJiewanDescriptor : MouDengAiOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseMouDengAiJiewan; }
internal sealed class GrowMouDengAiJiewanDescriptor : MouDengAiOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrowMouDengAiJiewan; }
internal sealed class MouDengAiOlHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouDengAiOlHost>().Start(f, op);
}
