namespace CardGame.Core;

/// <summary>A public claim of exact entities, retained by the still-resolving use.</summary>
public sealed record ProgramCurrentUsePhysicalClaim(long CardUseFrameId, long ActionId, long ProducerFrameId,
    int EffectIndex, int ActorSeat, int RecipientSeat, string SkillId, string SkillInstanceId,
    string GameplayHash, string TriggerId, IReadOnlyList<int> CardIds);
public sealed record ProgramCurrentUsePhysicalCardsClaimedEvent(ProgramCurrentUsePhysicalClaim Claim) : IGameEvent;

internal interface ICurrentUsePhysicalClaimProgramHost
{
    SkillProgramStepOutcome ClaimCurrentUsePhysicalCards(ProgramSkillFrame frame);
    void PreventCurrentTargetSlashCancellationByRule(ProgramSkillFrame frame);
}

internal abstract class CurrentTargetSlashOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor(),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
}

internal sealed class ClaimCurrentUsePhysicalCardsDescriptor : CurrentTargetSlashOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimCurrentUsePhysicalCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimCurrentUsePhysicalCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.CurrentUsePhysicalClaimValue());
}
internal sealed class ClaimCurrentUsePhysicalCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimCurrentUsePhysicalCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((ICurrentUsePhysicalClaimProgramHost)host).ClaimCurrentUsePhysicalCards(frame);
}

internal sealed class PreventCurrentTargetSlashCancellationByRuleDescriptor : CurrentTargetSlashOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule;
    public override ISkillProgramEffectHandler Handler { get; } = new PreventCurrentTargetSlashCancellationByRuleHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ProhibitCurrentResponse,
        static (_, context) => context.CurrentTargetCancellationByRuleValue());
}
internal sealed class PreventCurrentTargetSlashCancellationByRuleHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PreventCurrentTargetSlashCancellationByRule;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host)
    {
        ((ICurrentUsePhysicalClaimProgramHost)host).PreventCurrentTargetSlashCancellationByRule(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
