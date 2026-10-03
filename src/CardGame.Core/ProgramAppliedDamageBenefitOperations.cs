namespace CardGame.Core;

/// <summary>A committed one-time benefit owned by the exact applied-damage program.</summary>
public sealed record ProgramAppliedDamageBenefit(int InstructionIndex, long DamageWindowId, long DamageFrameId,
    int OwnerSeat, int TargetSeat, int? SourceSeat, int Amount, long FirstMovementSequence, long LastMovementSequence, int DrawCount);

public sealed record ProgramDistinctTurnTargetCommittedEvent(long FrameId, int OwnerSeat, string SkillId,
    string BindingId, string SkillInstanceId, string GameplayHash, string UsageId, int TargetSeat,
    int TurnNumber, int TurnSeat, long PhaseInstanceId) : IGameEvent;

internal interface IAppliedDamageBenefitProgramHost
{
    SkillProgramStepOutcome ReceiveOwnerDamage(ProgramSkillFrame frame, int amount);
    void ConsumeDistinctTurnTarget(ProgramSkillFrame frame, string usageId);
    SkillProgramStepOutcome DrawOwnerAtAppliedDamage(ProgramSkillFrame frame, int amount);
}

internal sealed class ReceiveOwnerDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReceiveOwnerDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new AppliedDamageBenefitHandler(SkillProgramEffectOp.ReceiveOwnerDamage);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.LoseHp,
        static (effect, context) => context.LoseHp(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            DrawProgramOperationDescriptor.Amount(r, 20), r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class ConsumeDistinctTurnTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumeDistinctTurnTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new AppliedDamageBenefitHandler(SkillProgramEffectOp.ConsumeDistinctTurnTarget);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.CaptureSelectedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "usageId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: r.RequiredIdentifier("usageId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}

internal sealed class DrawOwnerAtAppliedDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawOwnerAtAppliedDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new AppliedDamageBenefitHandler(SkillProgramEffectOp.DrawOwnerAtAppliedDamage);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r),
            DrawProgramOperationDescriptor.Amount(r, 20), r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

internal sealed class AppliedDamageBenefitHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        var applied = (IAppliedDamageBenefitProgramHost)host;
        if (op == SkillProgramEffectOp.ReceiveOwnerDamage) return applied.ReceiveOwnerDamage(frame, effect.Amount);
        if (op == SkillProgramEffectOp.DrawOwnerAtAppliedDamage) return applied.DrawOwnerAtAppliedDamage(frame, effect.Amount);
        applied.ConsumeDistinctTurnTarget(frame, effect.StateId!); return SkillProgramStepOutcome.Continue;
    }
}
