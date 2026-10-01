namespace CardGame.Core;

internal interface IIssuedPlayUseProgramHost
{
    void IssueCardNoResponseAndPlayUseBan(ProgramSkillFrame frame);
}
internal sealed class IssueCardNoResponseAndPlayUseBanDescriptor : ProgramOperationDescriptorBase, IActualPlayPhaseUseLedgerOperation
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan;
    public override ISkillProgramEffectHandler Handler { get; } = new IssueCardNoResponseAndPlayUseBanHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.IssuePlayUsePolicy, static (_, context) => context.IssuePlayUsePolicy());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, reader.RequiredEnum<SkillProgramEffectTarget>("target"), 0, reader.Condition());
        if (effect.Target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("Issued card policy requires the owner actor.");
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),
         new RequireContext(ProgramContextCapability.CardAction), new RequireCardActionActor()];
}
public sealed class IssueCardNoResponseAndPlayUseBanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    {
        ((IIssuedPlayUseProgramHost)host).IssueCardNoResponseAndPlayUseBan(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
