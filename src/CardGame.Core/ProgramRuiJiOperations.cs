namespace CardGame.Core;

// 巧力/清靓 share one host: both duel launches and both reactive payoffs need
// the exact program frame to authenticate their suspended prompts.
internal interface IRuiJiProgramHost
{
    SkillProgramStepOutcome QiaoliWeaponDuel(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliArmorDuel(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliEndingEquipmentGain(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QingliangChooseOption(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 巧力 first option: a weapon-slot hand/equipment card is used as a Duel; the
// damage payoff is a separate trigger keyed on the committed conversion source.
internal sealed class QiaoliWeaponDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliWeaponDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliDuelHandler(SkillProgramEffectOp.QiaoliWeaponDuel);
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.OtherRecipient;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.QiaoliDuel(unrespondable: false));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeSelectedCards(0), new ReadSelectedTarget()];
}

// 巧力 second option: a non-weapon equipment card is used as an unrespondable
// Duel; the end phase grants one random equipment per committed activation.
internal sealed class QiaoliArmorDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliArmorDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliDuelHandler(SkillProgramEffectOp.QiaoliArmorDuel);
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.OtherRecipient;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.QiaoliDuel(unrespondable: true));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeSelectedCards(0), new ReadSelectedTarget()];
}

public sealed class QiaoliDuelHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.QiaoliWeaponDuel
            ? ((IRuiJiProgramHost)host).QiaoliWeaponDuel(frame, effect)
            : ((IRuiJiProgramHost)host).QiaoliArmorDuel(frame, effect);
}

// 巧力 first-option payoff: the skill-committed Duel damaged its designated
// target; the owner draws the weapon's attack range and may distribute them.
internal sealed class QiaoliWeaponDamageDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliWeaponDamageDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliWeaponDamageDrawHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DamageAppliedBeforeDying)];
}

public sealed class QiaoliWeaponDamageDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliWeaponDamageDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IRuiJiProgramHost)host).QiaoliWeaponDamageDraw(f, e);
}

// 巧力 second-option payoff: one random equipment card per activation committed
// this turn, drawn deterministically from the deck.
internal sealed class QiaoliEndingEquipmentGainDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliEndingEquipmentGain;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliEndingEquipmentGainHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class QiaoliEndingEquipmentGainHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliEndingEquipmentGain;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IRuiJiProgramHost)host).QiaoliEndingEquipmentGain(f, e);
}

// 清靓: reveal the whole hand against a single-target damage card and pick
// mutual draws or a suit discard that nullifies the card against the owner.
internal sealed class QingliangChooseOptionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QingliangChooseOption;
    public override ISkillProgramEffectHandler Handler { get; } = new QingliangChooseOptionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect,
        static (_, context) => context.NullifyCurrentCardEffect());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
        new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Target,
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Duel,
             CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.FireAttack])
    ];
}

public sealed class QingliangChooseOptionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QingliangChooseOption;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IRuiJiProgramHost)host).QingliangChooseOption(f, e);
}

// Public-only pricing for the 巧力 launches: a Duel trades response risk against
// target pressure; the unrespondable variant removes the owner's loss branch.
internal sealed partial class ProgramAiEstimateContext
{
    internal void QiaoliDuel(bool unrespondable)
    {
        var otherHand = _publicContext.SelectedTarget?.HandCount ?? 3;
        var ownerHand = _player.HandCount;
        var ownerWin = Math.Clamp(0.5d + (ownerHand - otherHand) * 0.08d + (unrespondable ? 0.25d : 0d), 0.15d, 0.9d);
        _otherAdjustment -= (1d - ownerWin) * (unrespondable ? 6d : 12d);
        _targetAdjustment -= ownerWin * 20d;
    }
}
