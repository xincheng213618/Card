namespace CardGame.Core;

public interface IControlProgramEffectHost
{
    void ConsumeCategoryTargetLedger(ProgramSkillFrame frame, string usageId);
    void ReplaceCurrentCardUseActor(ProgramSkillFrame frame, int actorSeat);
    SkillProgramStepOutcome AddCurrentCardUseTarget(ProgramSkillFrame frame, int targetSeat);
    void ReduceCurrentDamage(ProgramSkillFrame frame, int amount);
}

internal sealed class ConsumeCategoryTargetLedgerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumeCategoryTargetLedger;
    public override ISkillProgramEffectHandler Handler { get; } = new ConsumeCategoryTargetLedgerHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.CaptureSelectedCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "usageId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), stateId: r.RequiredIdentifier("usageId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

internal abstract class CardUseRoleDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: a card-use role change requires selectedTarget.");
        return new(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
internal sealed class ReplaceCurrentCardUseActorDescriptor : CardUseRoleDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceCurrentCardUseActor;
    public override ISkillProgramEffectHandler Handler { get; } = new ReplaceCurrentCardUseActorHandler();
}
internal sealed class AddCurrentCardUseTargetDescriptor : CardUseRoleDescriptor
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddCurrentCardUseTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new AddCurrentCardUseTargetHandler();
}
internal sealed class ReduceCurrentDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReduceCurrentDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new ReduceCurrentDamageHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static (effect, context) => context.PreventCurrentDamage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var amount = r.RequiredInt("amount");
        if (amount <= 0 || amount > 20) throw new InvalidOperationException($"Invalid skill program at {r.Path}.amount: requires 1..20.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class ConsumeCategoryTargetLedgerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ConsumeCategoryTargetLedger;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IControlProgramEffectHost)host).ConsumeCategoryTargetLedger(frame, effect.StateId!);
        return SkillProgramStepOutcome.Continue;
    }
}
public sealed class ReplaceCurrentCardUseActorHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceCurrentCardUseActor;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IControlProgramEffectHost)host).ReplaceCurrentCardUseActor(frame, targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}
public sealed class AddCurrentCardUseTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AddCurrentCardUseTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        return ((IControlProgramEffectHost)host).AddCurrentCardUseTarget(frame, targetSeat);
    }
}
public sealed class ReduceCurrentDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReduceCurrentDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IControlProgramEffectHost)host).ReduceCurrentDamage(frame, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}
