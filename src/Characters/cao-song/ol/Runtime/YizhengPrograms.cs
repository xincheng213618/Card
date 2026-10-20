using System.Text.Json.Serialization;

namespace CardGame.Core;

internal sealed class GrantYizhengSupportDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantYizhengSupport;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoSongHandler(SkillProgramEffectOp.GrantYizhengSupport);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
}

public sealed record CaoSongYizhengGrantedEvent(long GrantId, CardConversionSource Source,
    int RecipientSeat, int IssuedTurn) : IGameEvent;
public sealed record CaoSongYizhengExpiredEvent(long GrantId, int OwnerSeat, int TurnNumber) : IGameEvent;
public sealed record CaoSongYizhengAmountPaidEvent(long ParentFrameId, long GrantId, int OwnerSeat,
    int BeneficiarySeat, bool Recovery, int MaximumHpBefore, int MaximumHpAfter) : IGameEvent;
public sealed record CaoSongYizhengAmountIncreasedEvent(long ParentFrameId, long GrantId,
    int BeneficiarySeat, bool Recovery, int Before, int After) : IGameEvent;
