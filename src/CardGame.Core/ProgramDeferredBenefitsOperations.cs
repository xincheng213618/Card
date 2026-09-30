namespace CardGame.Core;

public sealed record ProgramFactionRecoveryDraft(long DyingFrameId,
    IReadOnlyList<int> ResponderSeats, int ResponderIndex, IReadOnlyList<int> AcceptedSeats,
    bool SettlingDebts = false);
public sealed record ProgramWeaponDamageDraft(int TargetSeat, int WeaponCardId,
    int RequiredCount, IReadOnlyList<int> SelectedIds);
public sealed record ProgramFactionRecoveryDebt(long DyingFrameId, int OwnerSeat, int ResponderSeat,
    string SkillId, string BindingId, string SkillInstanceId);
public sealed record ProgramNextTurnRuleModifier(long ParentFrameId, int EffectIndex,
    int TargetSeat, CardUseEffectSource Source, SkillRuleQuery Query, int Amount);
public sealed record ProgramNextTurnRuleModifierQueuedEvent(ProgramNextTurnRuleModifier Modifier) : IGameEvent;
public sealed record ProgramFactionRecoveryChoiceEvent(long FrameId, string SkillId,
    int OwnerSeat, int ResponderSeat, bool Accepted, long DyingFrameId) : IGameEvent;
public sealed record ProgramWeaponDamageChoiceEvent(long FrameId, string SkillId, int OwnerSeat,
    int TargetSeat, int WeaponCardId, bool Discarded, IReadOnlyList<int> DiscardedCardIds,
    int DamageBefore, int DamageAfter) : IGameEvent;

internal interface IDeferredBenefitsProgramHost
{
    SkillProgramStepOutcome RequestFactionRecovery(ProgramSkillFrame frame, string factionId);
    void SetNextTurnRuleModifier(ProgramSkillFrame frame, int targetSeat, SkillRuleQuery query, int amount);
    SkillProgramStepOutcome WeaponDiscardOrDamageBonus(ProgramSkillFrame frame, int amount);
    void ResolveJudgmentColorBenefit(ProgramSkillFrame frame, int targetSeat, string sourceBind, int recovery);
}

internal sealed class RequestFactionRecoveryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestFactionRecovery;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestFactionRecoveryHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DyingRescue,
        static (effect, context) => context.DyingRescue(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "providerFactionId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1,
            r.Condition(), providerFactionId: r.RequiredIdentifier("providerFactionId"));
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];
}

internal sealed class SetNextTurnRuleModifierDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SetNextTurnRuleModifier;
    public override ISkillProgramEffectHandler Handler { get; } = new SetNextTurnRuleModifierHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "query", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var query = r.RequiredEnum<SkillRuleQuery>("query");
        var amount = r.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget ||
            query is not (SkillRuleQuery.HandLimit or SkillRuleQuery.SlashLimit) || amount is < 1 or > 20)
            throw new InvalidOperationException($"Invalid next-turn modifier at {r.Path}.");
        return new(Op, target, amount, r.Condition(), ruleQuery: query, ruleOperation: SkillRuleOperation.Add);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}

internal sealed class WeaponDiscardOrDamageBonusDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.WeaponDiscardOrDamageBonus;
    public override ISkillProgramEffectHandler Handler { get; } = new WeaponDiscardOrDamageBonusHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (effect, context) => context.Damage(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        var amount = r.RequiredInt("amount");
        if (amount is < 1 or > 20) throw new InvalidOperationException($"Invalid damage bonus at {r.Path}.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied), new RequireContext(ProgramContextCapability.Damage)];
}

internal sealed class ResolveJudgmentColorBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveJudgmentColorBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveJudgmentColorBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (effect, context) => context.Recover(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "amount", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        var amount = r.RequiredInt("amount");
        if (target != SkillProgramEffectTarget.SelectedTarget || amount is < 1 or > 20)
            throw new InvalidOperationException($"Invalid judgment color benefit at {r.Path}.");
        return new(Op, target, amount, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new ReadSingleCardSet(effect.SourceBind!), new RequireContext(ProgramContextCapability.Damage)];
}

public sealed class RequestFactionRecoveryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestFactionRecovery;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IDeferredBenefitsProgramHost)host).RequestFactionRecovery(f, e.ProviderFactionId!);
}
public sealed class SetNextTurnRuleModifierHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetNextTurnRuleModifier;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { ((IDeferredBenefitsProgramHost)host).SetNextTurnRuleModifier(f, seat, e.RuleQuery!.Value, e.Amount); return SkillProgramStepOutcome.Continue; }
}
public sealed class WeaponDiscardOrDamageBonusHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.WeaponDiscardOrDamageBonus;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IDeferredBenefitsProgramHost)host).WeaponDiscardOrDamageBonus(f, e.Amount);
}
public sealed class ResolveJudgmentColorBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveJudgmentColorBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host)
    { ((IDeferredBenefitsProgramHost)host).ResolveJudgmentColorBenefit(f, seat, e.SourceBind!, e.Amount); return SkillProgramStepOutcome.Continue; }
}
