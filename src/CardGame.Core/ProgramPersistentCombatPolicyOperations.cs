namespace CardGame.Core;

internal interface IPersistentCombatPolicyProgramHost
{
    void SetTurnHandLimitFromPlayDamage(ProgramSkillFrame frame);
    void GrantGameFactionAttackRangeTargets(ProgramSkillFrame frame, string factionId);
}

internal sealed class SetTurnHandLimitFromPlayDamageDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetTurnHandLimitFromPlayDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new SetTurnHandLimitFromPlayDamageHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

internal sealed class GrantGameFactionAttackRangeTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantGameFactionAttackRangeTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantGameFactionAttackRangeTargetsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "providerFactionId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            providerFactionId: r.RequiredIdentifier("providerFactionId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadTargetSet(1, 2)];
}

public sealed class SetTurnHandLimitFromPlayDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetTurnHandLimitFromPlayDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IPersistentCombatPolicyProgramHost)host).SetTurnHandLimitFromPlayDamage(frame);
        return SkillProgramStepOutcome.Continue;
    }
}
public sealed class GrantGameFactionAttackRangeTargetsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantGameFactionAttackRangeTargets;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IPersistentCombatPolicyProgramHost)host).GrantGameFactionAttackRangeTargets(frame, effect.ProviderFactionId!);
        return SkillProgramStepOutcome.Continue;
    }
}
