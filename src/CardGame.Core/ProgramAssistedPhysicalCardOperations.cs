namespace CardGame.Core;

public sealed record ProgramAssistedSlashRequest(int ActorSeat, int? TargetSeat = null, bool ActorChoosesTarget = false);
public sealed record ProgramOtherCardSlot(CardZoneKind Zone, int SlotIndex);
public sealed record ProgramOtherCardSelection(int SourceSeat, int RequiredCount, IReadOnlyList<ProgramOtherCardSlot> SelectedSlots);

internal sealed class RequestSlashAgainstChosenTargetProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashAgainstChosenTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestSlashAgainstChosenTargetSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RequestSlashByTarget, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "chooserRef", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("An assisted Slash requires a selected actor.");
        var chooser = r.Has("chooserRef") ? r.RequiredParticipantReference("chooserRef") : null;
        if (chooser is not null && chooser.Kind != ProgramParticipantRef.SelectedTarget) throw new InvalidOperationException("An assisted Slash chooser must be its selected actor.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"), chooserRef: chooser);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget(), new CreateChoiceResult(effect.ResultBind!, ["used-slash", "declined"])];
}
public sealed class RequestSlashAgainstChosenTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestSlashAgainstChosenTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.RequestSlashAgainstChosenTarget(frame, targetSeat, effect.ResultBind!, effect.ChooserRef?.Kind == ProgramParticipantRef.SelectedTarget);
}
internal sealed class TakeSelectedTargetCardsProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.TakeSelectedTargetCards;
    public override ISkillProgramEffectHandler Handler { get; } = new TakeSelectedTargetCardsSkillProgramEffectHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var count = r.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget || count is < 1 or > 2) throw new InvalidOperationException("Other-owned card selection requires one selected source and count 1..2.");
        return new SkillProgramEffect(Op, target, count, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
public sealed class TakeSelectedTargetCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TakeSelectedTargetCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.TakeSelectedTargetCards(frame, targetSeat, effect.Amount);
}
