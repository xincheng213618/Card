namespace CardGame.Core;

public enum GrantedEntityPhaseStage { Scheduled, Active, Returning, DamagePaid, Complete }
public sealed record GrantedEntityPhaseReceipt(CardConversionSource Source, int TurnNumber,
    int PhaseInstanceId, GrantedEntityPhaseStage Stage, int? CardId = null,
    int? ClaimMovementSequence = null, int? DamageDealt = null);
public sealed record GrantedPhaseSlashClaimReceipt(int InstructionIndex, long PhaseProducerFrameId,
    int PhaseInstanceId, bool Paid, int? CardId = null, int? ClaimMovementSequence = null);
public sealed record GrantedEntityPhaseStartedEvent(long FrameId, CardConversionSource Source,
    int TurnNumber, int PhaseInstanceId) : IGameEvent;
public sealed record GrantedPhaseSlashClaimedEvent(long PhaseProducerFrameId, long ClaimProducerFrameId,
    CardConversionSource Source, int PhaseInstanceId, int CardId, int MovementSequence) : IGameEvent;
public sealed record GrantedEntityPhaseEndedEvent(long FrameId, CardConversionSource Source,
    int TurnNumber, int PhaseInstanceId, int DamageDealt) : IGameEvent;
public sealed record GrantedEntityDistanceUseIssued(long CardUseFrameId, long CardActionId,
    long PhaseProducerFrameId, CardConversionSource Source, int PhaseInstanceId, int CardId);
public sealed record GrantedEntityDistanceUseIssuedEvent(GrantedEntityDistanceUseIssued Policy) : IGameEvent;

internal interface IGrantedEntityPhaseProgramHost
{
    SkillProgramStepOutcome InsertGrantedEntityPlayPhase(ProgramSkillFrame frame);
    SkillProgramStepOutcome ClaimGrantedPhaseSlash(ProgramSkillFrame frame);
}
internal abstract class GrantedEntityPhaseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target");
        return new(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            new(SkillProgramConditionKind.Always, 0, []));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.InsertGrantedEntityPlayPhase
            ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.PlayPhaseStarting)];
}
internal sealed class InsertGrantedEntityPlayPhaseDescriptor : GrantedEntityPhaseDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.InsertGrantedEntityPlayPhase;
    public override ISkillProgramEffectHandler Handler { get; } = new InsertGrantedEntityPlayPhaseHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.InsertPhase,
        static (_, c) => c.InsertPhase(new(SkillProgramEffectOp.InsertPhase, SkillProgramEffectTarget.Owner,
            0, new(SkillProgramConditionKind.Always, 0, []), phase: TurnPhase.Play,
            phaseContinuation: SkillProgramPhaseContinuation.BeforeNormalPreparation)));
}
internal sealed class ClaimGrantedPhaseSlashDescriptor : GrantedEntityPhaseDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimGrantedPhaseSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimGrantedPhaseSlashHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1,
            new(SkillProgramConditionKind.Always, 0, []))));
}
public sealed class InsertGrantedEntityPlayPhaseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InsertGrantedEntityPlayPhase;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IGrantedEntityPhaseProgramHost)h).InsertGrantedEntityPlayPhase(f);
}
public sealed class ClaimGrantedPhaseSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimGrantedPhaseSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IGrantedEntityPhaseProgramHost)h).ClaimGrantedPhaseSlash(f);
}

internal static class GrantedEntityPhaseComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject,
        SkillProgramTurnOwnerScope scope)
    {
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.InsertGrantedEntityPlayPhase or SkillProgramEffectOp.ClaimGrantedPhaseSlash)) return;
        if (effects.Count != 1 || subject != SkillProgramTriggerSubject.Owner || scope != SkillProgramTurnOwnerScope.Own ||
            effects[0].Op == SkillProgramEffectOp.InsertGrantedEntityPlayPhase && window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            effects[0].Op == SkillProgramEffectOp.ClaimGrantedPhaseSlash && window != SkillProgramTriggerWindow.PlayPhaseStarting)
            throw new InvalidOperationException($"Invalid skill program at {path}: a granted-entity phase operation requires its standalone exact own starting window.");
    }
}
