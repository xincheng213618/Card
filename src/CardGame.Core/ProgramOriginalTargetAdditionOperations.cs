namespace CardGame.Core;

public sealed record OriginalTargetAdditionGrant(int TurnNumber, int TurnSeat, long ParentFrameId,
    int EffectIndex, CardUseEffectSource Source, int TargetSeat, string GameplayHash,
    DirectedTurnCardPolicyEffect IssuedDirectedEffects = DirectedTurnCardPolicyEffect.None);
public sealed record OriginalTargetAdditionGrantedEvent(OriginalTargetAdditionGrant Grant) : IGameEvent;
public sealed record OriginalTargetAdditionIssuedEvent(long CardUseFrameId, long ActionId, OriginalTargetAdditionGrant Grant) : IGameEvent;
public sealed record OriginalTargetAdditionResolvedEvent(long CardUseFrameId, long ActionId, int ActorSeat, int TargetSeat, bool Added) : IGameEvent;
public sealed record OriginalTargetAdditionReceipt(IReadOnlyList<OriginalTargetAdditionGrant> Grants, bool Added = false);
public sealed record OriginalTargetAdditionDraft(long CardUseFrameId, long ActionId);

internal interface IOriginalTargetAdditionProgramHost
{
    void GrantOriginalTargetAddition(ProgramSkillFrame frame, int targetSeat);
    SkillProgramStepOutcome OfferOriginalTargetAddition(ProgramSkillFrame frame);
}
internal sealed class GrantTurnOriginalTargetAdditionDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnOriginalTargetAddition;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnOriginalTargetAdditionHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, context) => context.PublicControlValue(8d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException("An original-target grant requires the selected original target.");
        return new(Op, SkillProgramEffectTarget.SelectedTarget, 0, reader.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
public sealed class GrantTurnOriginalTargetAdditionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnOriginalTargetAddition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((IOriginalTargetAdditionProgramHost)host).GrantOriginalTargetAddition(frame, targetSeat); return SkillProgramStepOutcome.Continue; }
}
internal sealed class OfferOriginalTargetAdditionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferOriginalTargetAddition;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferOriginalTargetAdditionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, context) => context.PublicControlValue(4d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
}
public sealed class OfferOriginalTargetAdditionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferOriginalTargetAddition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IOriginalTargetAdditionProgramHost)host).OfferOriginalTargetAddition(frame);
}
