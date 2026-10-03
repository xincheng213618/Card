namespace CardGame.Core;

/// <summary>
/// Owner-owned draft for the current OL 界孟获 再起-style ending choice: up to a
/// turn-scoped red-discard budget of other characters each choose between drawing
/// one card and recovering the owner by one. The selection is committed through a
/// public fact before any beneficiary is asked.
/// </summary>
public sealed record ProgramRedDiscardRecoveryDraft(int Budget, IReadOnlyList<int> Selected,
    bool Committed = false, int Cursor = 0, int? AskingSeat = null);
public sealed record ProgramRedDiscardRecoveryCommittedEvent(long FrameId, int OwnerSeat, int Budget,
    IReadOnlyList<int> Seats) : IGameEvent;
public sealed record ProgramRedDiscardRecoveryChosenEvent(long FrameId, int OwnerSeat, int ChooserSeat,
    string Option) : IGameEvent;

internal interface IRedDiscardRecoveryHost
{
    SkillProgramStepOutcome OfferRedDiscardRecoveryChoice(ProgramSkillFrame frame);
}
internal sealed class OfferRedDiscardRecoveryChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferRedDiscardRecoveryChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferRedDiscardRecoveryChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}
public sealed class OfferRedDiscardRecoveryChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferRedDiscardRecoveryChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IRedDiscardRecoveryHost)h).OfferRedDiscardRecoveryChoice(f);
}
