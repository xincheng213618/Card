namespace CardGame.Core;

public sealed record MouZhuRongLierenWonEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long UseId, long ActionId, int OpponentSeat) : IGameEvent;
public sealed record MouZhuRongLierenDamageChosenEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, int TargetSeat) : IGameEvent;
public sealed record MouZhuRongAssaultUsedEvent(int TurnOrdinal, int ActorSeat, long UseId) : IGameEvent;
public sealed record MouZhuRongAssaultGeneratedEvent(long FrameId, CardConversionSource Source, int RecipientSeat,
    int CardId, long RandomBefore, long RandomAfter) : IGameEvent;

internal interface IMouZhuRongHost
{
    void RecordLieren(ProgramSkillFrame frame);
    SkillProgramStepOutcome SelectLierenTarget(ProgramSkillFrame frame);
    SkillProgramStepOutcome GiftAssault(ProgramSkillFrame frame);
}
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
internal sealed class MouZhuRongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    {
        var host = h.GetCapability<IMouZhuRongHost>();
        if (op == SkillProgramEffectOp.RecordMouZhuRongLieren) { host.RecordLieren(f); return SkillProgramStepOutcome.Continue; }
        return op == SkillProgramEffectOp.SelectMouZhuRongLierenTarget ? host.SelectLierenTarget(f) : host.GiftAssault(f);
    }
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
