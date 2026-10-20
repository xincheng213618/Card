using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouHuaXiongYaowuReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long AttackId, int OwnerSeat, int? SourceSeat, CardKind EffectiveKind,
    bool IsRed, string? Benefit = null);
public sealed record MouHuaXiongYaowuStartedEvent(long FrameId, long WindowId, long AttackId,
    int OwnerSeat, int? SourceSeat, CardKind EffectiveKind, bool IsRed) : IGameEvent;
public sealed record MouHuaXiongYaowuBenefitChosenEvent(long FrameId, int? RecipientSeat, string Benefit) : IGameEvent;
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
