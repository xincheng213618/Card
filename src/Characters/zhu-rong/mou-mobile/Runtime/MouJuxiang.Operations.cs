namespace CardGame.Core;
public sealed record MouZhuRongAssaultUsedEvent(int TurnOrdinal, int ActorSeat, long UseId) : IGameEvent;
public sealed record MouZhuRongAssaultGeneratedEvent(long FrameId, CardConversionSource Source, int RecipientSeat,
    int CardId, long RandomBefore, long RandomAfter) : IGameEvent;
internal sealed class GiftMouZhuRongOutsideAssaultDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiftMouZhuRongOutsideAssault;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget()];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhuRongHandler(SkillProgramEffectOp.GiftMouZhuRongOutsideAssault);
}
