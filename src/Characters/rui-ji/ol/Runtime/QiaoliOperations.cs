namespace CardGame.Core;

// 巧力 first option: a weapon-slot hand/equipment card is used as a Duel; the
// damage payoff is a separate trigger keyed on the committed conversion source.
internal sealed class QiaoliWeaponDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliWeaponDuel;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliDuelHandler(SkillProgramEffectOp.QiaoliWeaponDuel);
    public override ProgramOperationLegalityPolicy LegalityPolicy => ProgramOperationLegalityPolicy.OtherRecipient;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.QiaoliDuel(unrespondable: false));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ConsumeSelectedCards(0), new ReadSelectedTarget()];
}

internal sealed class QiaoliDuelHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        op == SkillProgramEffectOp.QiaoliWeaponDuel
            ? host.GetCapability<IRuiJiProgramHost>().QiaoliWeaponDuel(frame, effect)
            : host.GetCapability<IRuiJiProgramHost>().QiaoliArmorDuel(frame, effect);
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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}

public sealed class QiaoliWeaponDamageDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliWeaponDamageDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRuiJiProgramHost>().QiaoliWeaponDamageDraw(f, e);
}

// 巧力 second-option payoff: one random equipment card per activation committed
// this turn, drawn deterministically from the deck.
internal sealed class QiaoliEndingEquipmentGainDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliEndingEquipmentGain;
    public override ISkillProgramEffectHandler Handler { get; } = new QiaoliEndingEquipmentGainHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}

public sealed class QiaoliEndingEquipmentGainHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.QiaoliEndingEquipmentGain;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRuiJiProgramHost>().QiaoliEndingEquipmentGain(f, e);
}
