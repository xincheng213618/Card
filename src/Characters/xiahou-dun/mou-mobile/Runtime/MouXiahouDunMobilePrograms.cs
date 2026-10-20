using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum MouXiahouDunMobileStage { SelectTargets, Damaging, SelectRecipient, MovementChildren }
public sealed record MouXiahouDunMobileReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op)
{
    public MouXiahouDunMobileStage Stage { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public PublicPersistentPileSource? Pile { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>()), _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public int Index { get; init; }
    public int RecipientSeat { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouXiahouDunMobileReceipt? MouXiahouDunMobile { get; init; } }
public sealed record MouXiahouDunMobileStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, ActualDiscardRecoveryPhaseKey? Phase) : IGameEvent;
public sealed record MouXiahouDunMobileDiscardOriginEvent(long BatchId, long MovementSequence, int CardId, bool TrueUse) : IGameEvent;
public sealed record MouXiahouDunMobileGanglieSpentEvent(long FrameId, int OwnerSeat, int TargetSeat, int Index) : IGameEvent;
public sealed record MouXiahouDunMobileDamageIssuedEvent(long FrameId, int Index, int SourceSeat, int TargetSeat) : IGameEvent;
public sealed record MouXiahouDunMobileCollectedEvent(long FrameId, CardConversionSource Source, int CardId, long DiscardSequence, long ClaimSequence, int Capacity) : IGameEvent;
public sealed record MouXiahouDunMobileRosterEvent(long FrameId, int Index, int CardId, CardLocation From) : IGameEvent;
public sealed record MouXiahouDunMobileGiftEvent(long FrameId, int Index, int CardId, int RecipientSeat, long Sequence) : IGameEvent;
public sealed record MouXiahouDunMobileMovementEvent(long FrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
internal interface IMouXiahouDunMobileHost { SkillProgramStepOutcome Start(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class MouXiahouDunMobileDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new MouXiahouDunMobileHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.CollectMouXiahouDunQingjian => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived)],
        SkillProgramEffectOp.DistributeMouXiahouDunQingjian => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)],
        _ => []
    };
}
internal sealed class ResolveMouXiahouDunGanglieDescriptor : MouXiahouDunMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouXiahouDunGanglie; }
internal sealed class CollectMouXiahouDunQingjianDescriptor : MouXiahouDunMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.CollectMouXiahouDunQingjian; }
internal sealed class DistributeMouXiahouDunQingjianDescriptor : MouXiahouDunMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributeMouXiahouDunQingjian; }
internal sealed class MouXiahouDunMobileHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouXiahouDunMobileHost>().Start(f, op);
}
