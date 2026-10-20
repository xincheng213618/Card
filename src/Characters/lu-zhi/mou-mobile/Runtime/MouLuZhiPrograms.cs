using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouLuZhiStage { Drawing, DrawChildren, RenChoice, TargetChoice, CostChoice, Paying, MovementChildren, DamageIssued }
public sealed record MouLuZhiRen(CardConversionSource Source, string GameplayHash, int CardId, CardLocation Location, long Sequence);
public sealed record MouLuZhiRenStoredEvent(MouLuZhiRen Ren) : IGameEvent;
public sealed record MouLuZhiRenRemovedEvent(MouLuZhiRen Ren) : IGameEvent;
public sealed record MouLuZhiStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long? ActionId, ActualDiscardRecoveryPhaseKey? Phase) : IGameEvent;
public sealed record MouLuZhiPaidEvent(long FrameId, int TargetSeat, int Count, bool? IsRed, long Before, long After) : IGameEvent;
public sealed record MouLuZhiMovementEvent(long FrameId, int CardId, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;
public sealed record MouLuZhiDrawReturnedEvent(long FrameId, int RecipientSeat, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouLuZhiReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op)
{
    public MouLuZhiStage Stage { get; init; }
    public MouLuZhiRen? Ren { get; init; }
    public long? ActionId { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public int TargetSeat { get; init; }
    public int RequiredCards { get; init; }
    public bool? IsRed { get; init; }
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    public long Before { get; init; }
    public long After { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouLuZhiReceipt? MouLuZhi { get; init; } }
internal interface IMouLuZhiHost { SkillProgramStepOutcome Start(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class MouLuZhiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override bool UsesConversionPolarity => Op is SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang or SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new MouLuZhiHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.InitializeMouLuZhiMingren => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting)],
        SkillProgramEffectOp.ExchangeMouLuZhiMingren => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)],
        SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin => [new RequireContext(ProgramContextCapability.CardAction)],
        _ => []
    };
}
internal sealed class InitializeMouLuZhiMingrenDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouLuZhiMingren; }
internal sealed class ExchangeMouLuZhiMingrenDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangeMouLuZhiMingren; }
internal sealed class ResolveMouLuZhiZhenliangYangDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiZhenliangYang; }
internal sealed class ResolveMouLuZhiZhenliangYinDescriptor : MouLuZhiDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuZhiZhenliangYin; }
internal sealed class MouLuZhiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouLuZhiHost>().Start(f, op);
}
