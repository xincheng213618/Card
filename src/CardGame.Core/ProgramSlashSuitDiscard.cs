namespace CardGame.Core;

public enum ProgramSlashSuitDiscardStage { Judging, JudgmentMovement, Choosing, PaymentMovement }
public sealed record ProgramSlashSuitDiscardDraft(long CardUseFrameId, long CardActionId, int TargetSeat,
    string JudgmentReason, string ResultBind, ProgramSlashSuitDiscardStage Stage,
    long? JudgmentFrameId = null, Suit? FinalSuit = null, int? PaidCardId = null,
    CardLocation? PaidFrom = null, long? PaymentMovementSequence = null);
public sealed record ProgramSlashSuitCancellationRestriction(long ProgramFrameId, int TargetSeat);
public sealed record ProgramSlashSuitDiscardResolvedEvent(long ProgramFrameId, string SkillId, int OwnerSeat,
    long CardUseFrameId, int TargetSeat, Suit? JudgmentSuit, int? PaidCardId, bool PaymentCompleted) : IGameEvent;

internal interface ISlashSuitDiscardProgramHost
{
    SkillProgramStepOutcome SuppressCurrentSlashTargetAndJudgeSuitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class SuppressCurrentSlashTargetAndJudgeSuitDiscardDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard;
    public override ISkillProgramEffectHandler Handler { get; } = new SuppressCurrentSlashTargetAndJudgeSuitDiscardHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction | ProgramContextCapability.Judgment;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ProhibitCurrentResponse,
        static (_, context) => context.ProhibitCurrentResponse());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "judgmentReason", "resultBind", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid program at {r.Path}: requires owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, r.Condition(),
            judgmentReason: r.RequiredIdentifier("judgmentReason"), resultBind: r.RequiredIdentifier("resultBind"),
            visibility: SkillProgramCardSetVisibility.Public);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.SlashBeforeResponse), new RequireCardActionActor(),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
}
public sealed class SuppressCurrentSlashTargetAndJudgeSuitDiscardHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeSuitDiscard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => ((ISlashSuitDiscardProgramHost)host).SuppressCurrentSlashTargetAndJudgeSuitDiscard(frame, effect);
}
