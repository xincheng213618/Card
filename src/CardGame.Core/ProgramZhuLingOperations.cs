namespace CardGame.Core;

internal interface IZhuLingProgramHost
{
    SkillProgramStepOutcome ZhuLingZhanyiChooseCategory(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuLingZhanyiEquipmentPunish(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 战意 launch: at the play phase start the owner discards every owned card of
// one chosen category; the other two categories stay empowered until the
// owner's next turn starts (state expiry is the content's own trigger).
internal sealed class ZhuLingZhanyiChooseCategoryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiChooseCategory;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuLingZhanyiChooseCategoryHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (effect, context) => context.ZhanyiLaunch(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class ZhuLingZhanyiChooseCategoryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiChooseCategory;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IZhuLingProgramHost)host).ZhuLingZhanyiChooseCategory(f, e);
}

// 战意 equipment punish: an equipment card the owner used is entering the
// owner's equipment zone; the owner may make another character discard one card.
internal sealed class ZhuLingZhanyiEquipmentPunishDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuLingZhanyiEquipmentPunishHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,
        static (effect, context) => context.ChooseOtherOwnedCardDiscard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),
        new RequireCardActionActor()
    ];
}

public sealed class ZhuLingZhanyiEquipmentPunishHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IZhuLingProgramHost)host).ZhuLingZhanyiEquipmentPunish(f, e);
}

// Public-only pricing for the 战意 launch: the visible buff is a turn-long
// offensive posture; the cost is at least one discarded owned card.
internal sealed partial class ProgramAiEstimateContext
{
    internal void ZhanyiLaunch(SkillProgramEffect effect)
    {
        _otherAdjustment += 6d;
        _estimatedHandCount = Math.Max(0, _estimatedHandCount - 1d);
    }
}
