using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouHuaXiongYangweiReceipt(CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualOwner, int PlayPhaseId, bool WeiResolved = false);
public sealed record MouHuaXiongYaowuReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long AttackId, int OwnerSeat, int? SourceSeat, CardKind EffectiveKind,
    bool IsRed, string? Benefit = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuaXiongYangweiReceipt? MouHuaXiongYangwei { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouHuaXiongYaowuReceipt? MouHuaXiongYaowu { get; init; }
}
public sealed record MouHuaXiongYangweiReservedEvent(long FrameId, int OwnerSeat, int ActualTurn,
    int PlayPhaseId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouHuaXiongWeiGrantedEvent(long FrameId, int OwnerSeat, int ActualTurn,
    int PlayPhaseId, bool ExpiredBeforeGrant) : IGameEvent;
public sealed record MouHuaXiongCooldownRecoveredEvent(long FrameId, int OwnerSeat, int IssuedActualTurn,
    int EndingActualTurn) : IGameEvent;
public sealed record MouHuaXiongYaowuStartedEvent(long FrameId, long WindowId, long AttackId,
    int OwnerSeat, int? SourceSeat, CardKind EffectiveKind, bool IsRed) : IGameEvent;
public sealed record MouHuaXiongYaowuBenefitChosenEvent(long FrameId, int? RecipientSeat, string Benefit) : IGameEvent;

internal interface IMouHuaXiongHost
{
    SkillProgramStepOutcome Yangwei(ProgramSkillFrame frame);
    SkillProgramStepOutcome Yaowu(ProgramSkillFrame frame);
}
internal sealed class ResolveMouHuaXiongYangweiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuaXiongYangwei;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouHuaXiongHandler(SkillProgramEffectOp.ResolveMouHuaXiongYangwei);
}
internal sealed class ResolveMouHuaXiongYaowuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuaXiongYaowu;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new CreateChoiceResult("yaowu-benefit", ["source-recover", "source-draw", "owner-draw", "none"])];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouHuaXiongHandler(SkillProgramEffectOp.ResolveMouHuaXiongYaowu);
}
internal sealed class MouHuaXiongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.ResolveMouHuaXiongYangwei ? host.GetCapability<IMouHuaXiongHost>().Yangwei(f) : host.GetCapability<IMouHuaXiongHost>().Yaowu(f);
}
