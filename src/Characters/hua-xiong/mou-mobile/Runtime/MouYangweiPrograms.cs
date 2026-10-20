using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouHuaXiongYangweiReceipt(CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualOwner, int PlayPhaseId, bool WeiResolved = false);
public sealed record MouHuaXiongYangweiReservedEvent(long FrameId, int OwnerSeat, int ActualTurn,
    int PlayPhaseId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record MouHuaXiongWeiGrantedEvent(long FrameId, int OwnerSeat, int ActualTurn,
    int PlayPhaseId, bool ExpiredBeforeGrant) : IGameEvent;
public sealed record MouHuaXiongCooldownRecoveredEvent(long FrameId, int OwnerSeat, int IssuedActualTurn,
    int EndingActualTurn) : IGameEvent;
internal sealed class ResolveMouHuaXiongYangweiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuaXiongYangwei;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouHuaXiongHandler(SkillProgramEffectOp.ResolveMouHuaXiongYangwei);
}
