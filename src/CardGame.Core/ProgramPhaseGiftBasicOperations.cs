namespace CardGame.Core;

public sealed record ProgramPhaseGiftReceipt(int InstructionIndex, int PhaseInstanceId, int PreviousCount, int ActualCount);

internal interface IPhaseGiftBasicProgramHost
{
    void AccumulatePaidPhaseGift(ProgramSkillFrame frame, string resultBind, int threshold);
    SkillProgramStepOutcome OfferVirtualBasicCard(ProgramSkillFrame frame);
}

internal sealed class AccumulatePaidPhaseGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulatePaidPhaseGift;
    public override ISkillProgramEffectHandler Handler { get; } = new AccumulatePaidPhaseGiftHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GiveSelected, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "threshold", "condition");
        var threshold = r.RequiredInt("threshold");
        if (threshold is < 1 or > 64) throw new InvalidOperationException("A paid gift threshold must be 1..64.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), threshold, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new CreateChoiceResult(effect.ResultBind!, ["crossed", "not-crossed"])];
}

public sealed class AccumulatePaidPhaseGiftHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AccumulatePaidPhaseGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    {
        ((IPhaseGiftBasicProgramHost)host).AccumulatePaidPhaseGift(frame, effect.ResultBind!, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed class OfferVirtualBasicCardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferVirtualBasicCard;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferVirtualBasicCardHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class OfferVirtualBasicCardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferVirtualBasicCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => ((IPhaseGiftBasicProgramHost)host).OfferVirtualBasicCard(frame);
}
