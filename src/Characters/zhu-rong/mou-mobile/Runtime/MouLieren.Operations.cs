namespace CardGame.Core;

public sealed record MouZhuRongLierenWonEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int OpponentSeat) : IGameEvent;
public sealed record MouZhuRongLierenDamageChosenEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, int TargetSeat) : IGameEvent;
internal sealed class RecordMouZhuRongLierenDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordMouZhuRongLieren;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new ReadPindianResult("contest")];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.StartPindian,
        static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhuRongHandler(SkillProgramEffectOp.RecordMouZhuRongLieren);
}
internal sealed class SelectMouZhuRongLierenTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectMouZhuRongLierenTarget;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted), new ReadSelectedTarget()];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, c) => c.PublicControlValue(6));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhuRongHandler(SkillProgramEffectOp.SelectMouZhuRongLierenTarget);
}

internal interface IMouZhuRongLierenTargets : IProgramSelectionQuery
{
    IReadOnlyList<int> OtherTargets(int ownerSeat);
}
[ProgramTargetResolver(SkillProgramTargetKind.MouZhuRongLierenOther)]
internal sealed class MouZhuRongLierenOtherResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) =>
        queries.Get<IMouZhuRongLierenTargets>().OtherTargets(context.OwnerSeat);
}
