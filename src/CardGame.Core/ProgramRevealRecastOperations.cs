namespace CardGame.Core;

internal interface IRevealRecastProgramHost
{
    SkillProgramStepOutcome RecastSelectedCards(ProgramSkillFrame frame, string thresholdStateId, int threshold);
    void DrawCompletedCardParticipants(ProgramSkillFrame frame, string thresholdStateId, int threshold);
    SkillProgramStepOutcome RevealSelectedHandAgainstTarget(ProgramSkillFrame frame, string resultBind);
}

internal sealed class RecastSelectedCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedCards;
    public override ISkillProgramEffectHandler Handler { get; } = new RecastSelectedCardsProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "threshold", "condition");
        var threshold = r.RequiredInt("threshold");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || threshold is < 1 or > 64)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: recasting requires owner and a threshold in 1..64.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, threshold, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ConsumeSelectedCards(1)];
}

internal sealed class DrawCompletedCardParticipantsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawCompletedCardParticipants;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawCompletedCardParticipantsProgramHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "threshold", "condition");
        var threshold = r.RequiredInt("threshold");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner || threshold is < 1 or > 64)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: participant draws require owner and a threshold in 1..64.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, threshold, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}

internal sealed class RevealSelectedHandAgainstTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealSelectedHandAgainstTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealSelectedHandAgainstTargetProgramHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Reveal, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: paired reveals require owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 1, r.Condition(),
            resultBind: r.RequiredIdentifier("resultBind"), chooserRef: new(ProgramParticipantRef.SelectedTarget));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new CreateChoiceResult(effect.ResultBind!, ["damage", "obtain", "none"])];
}

public sealed class RecastSelectedCardsProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IRevealRecastProgramHost)host).RecastSelectedCards(frame, effect.StateId!, effect.Amount);
}

public sealed class DrawCompletedCardParticipantsProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawCompletedCardParticipants;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IRevealRecastProgramHost)host).DrawCompletedCardParticipants(frame, effect.StateId!, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class RevealSelectedHandAgainstTargetProgramHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealSelectedHandAgainstTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IRevealRecastProgramHost)host).RevealSelectedHandAgainstTarget(frame, effect.ResultBind!);
}
