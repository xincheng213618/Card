namespace CardGame.Core;

internal interface ICurrentTurnShownCardProgramHost
{
    void RevealOwnedBoundCardAppearance(ProgramSkillFrame frame, string bind);
    void IssueCurrentTurnNonLockedSkillSuppression(ProgramSkillFrame frame, int targetSeat);
    void GrantCurrentTurnDirectedHeartSlashBonus(ProgramSkillFrame frame, int targetSeat);
}
internal abstract class CurrentTurnShownCardDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new CurrentTurnShownCardHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var reveal = Op == SkillProgramEffectOp.RevealOwnedBoundCardAppearance;
        if (r.Has("sourceBind") != reveal) throw new InvalidOperationException("Only bound-card reveal accepts a source bind.");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != (reveal ? SkillProgramEffectTarget.Owner : SkillProgramEffectTarget.SelectedTarget))
            throw new InvalidOperationException("Shown-card turn effects require their declared participant.");
        return new(Op, target, 0, r.Condition(), sourceBind: reveal ? r.RequiredIdentifier("sourceBind") : null);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        e.SourceBind is { } bind ? [new ReadCardSet(bind)] : [new ReadSelectedTarget()];
}
internal sealed class RevealOwnedBoundCardAppearanceDescriptor : CurrentTurnShownCardDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealOwnedBoundCardAppearance; }
internal sealed class IssueCurrentTurnNonLockedSkillSuppressionDescriptor : CurrentTurnShownCardDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.IssueCurrentTurnNonLockedSkillSuppression; }
internal sealed class GrantCurrentTurnDirectedHeartSlashBonusDescriptor : CurrentTurnShownCardDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantCurrentTurnDirectedHeartSlashBonus; }
internal sealed class CurrentTurnShownCardHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    {
        var h = (ICurrentTurnShownCardProgramHost)host;
        if (op == SkillProgramEffectOp.RevealOwnedBoundCardAppearance) h.RevealOwnedBoundCardAppearance(f, e.SourceBind!);
        else if (op == SkillProgramEffectOp.IssueCurrentTurnNonLockedSkillSuppression) h.IssueCurrentTurnNonLockedSkillSuppression(f, seat);
        else h.GrantCurrentTurnDirectedHeartSlashBonus(f, seat);
        return SkillProgramStepOutcome.Continue;
    }
}
