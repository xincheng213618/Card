namespace CardGame.Core;

internal interface INextSlashProgramHost
{
    void RecoverToMaximum(ProgramSkillFrame frame);
    void DrawRecoveryReceipt(ProgramSkillFrame frame);
    void ReserveNextSlashDamage(ProgramSkillFrame frame);
}

internal abstract class NextSlashOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: requires owner.");
        return new(Op, SkillProgramEffectTarget.Owner, 0, new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ReserveNextSlashDamage
            ? SkillProgramTriggerWindow.SlashFullyDodged : SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}

internal sealed class RecoverToMaximumOperationDescriptor : NextSlashOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverToMaximum;
    public override ISkillProgramEffectHandler Handler { get; } = new RecoverToMaximumHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.RecoverTo,
        static (_, context) => context.RecoverTo(new(SkillProgramEffectOp.RecoverTo, SkillProgramEffectTarget.Owner,
            0, new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []), numberExpression: SkillProgramNumberExpression.IntegerConstant, minimumValue: 20, clampToMaxHp: true)));
}
internal sealed class DrawRecoveryReceiptOperationDescriptor : NextSlashOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawRecoveryReceipt;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawRecoveryReceiptHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 0, new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []), numberExpression: SkillProgramNumberExpression.OwnerLostHp)));
}
internal sealed class ReserveNextSlashDamageOperationDescriptor : NextSlashOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReserveNextSlashDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new ReserveNextSlashDamageHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardDamageModifier,
        static (_, context) => context.GrantTurnCardDamageModifier(new(SkillProgramEffectOp.GrantTurnCardDamageModifier, SkillProgramEffectTarget.Owner, 1, new SkillProgramCondition(SkillProgramConditionKind.Always, 0, []))));
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
}
internal sealed class RecoverToMaximumHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverToMaximum;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((INextSlashProgramHost)host).RecoverToMaximum(frame); return SkillProgramStepOutcome.Continue; }
}
internal sealed class DrawRecoveryReceiptHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawRecoveryReceipt;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((INextSlashProgramHost)host).DrawRecoveryReceipt(frame); return SkillProgramStepOutcome.Continue; }
}
internal sealed class ReserveNextSlashDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReserveNextSlashDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { ((INextSlashProgramHost)host).ReserveNextSlashDamage(frame); return SkillProgramStepOutcome.Continue; }
}
