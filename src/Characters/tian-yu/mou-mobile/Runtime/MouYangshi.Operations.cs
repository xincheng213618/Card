using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record MouTianYuRangeIncreasedEvent(long FrameId, int OwnerSeat, CardConversionSource Source, string GameplayHash, int RangeBefore) : IGameEvent;
internal sealed class ResolveMouTianYuYangshiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouTianYuYangshi;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new MouTianYuHandler(SkillProgramEffectOp.ResolveMouTianYuYangshi);
}
