namespace CardGame.Core;

internal interface IZhuLingProgramHost
{
    SkillProgramStepOutcome ZhuLingZhanyiChooseCategory(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuLingZhanyiEquipmentPunish(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 战意 launch: at the play phase start the owner discards every owned card of
// one chosen category; the other two categories stay empowered until the
// owner's next turn starts. New content opts into an actual-turn state lease;
// unconfigured content retains its original trigger-based expiry.
internal sealed class ZhuLingZhanyiChooseCategoryDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiChooseCategory;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuLingZhanyiChooseCategoryHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (effect, context) => context.ZhanyiLaunch(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition", "recoveryBonus", "expires", "expireBooleanStatesAtOwnerTurnStart");
        var configured = r.Has("recoveryBonus") || r.Has("expires") || r.Has("expireBooleanStatesAtOwnerTurnStart");
        var recoveryBonus = r.Has("recoveryBonus") ? r.RequiredInt("recoveryBonus") : 0;
        if (recoveryBonus is < 0 or > 20)
            throw new InvalidOperationException(r.Path + ": recoveryBonus must be between zero and twenty.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition())
        {
            CardUseBenefitPolicy = configured ? new(recoveryBonus,
                r.Has("expires") ? r.RequiredEnum<SkillProgramDamageModifierExpiration>("expires") : SkillProgramDamageModifierExpiration.CurrentTurnEnd,
                r.Has("expireBooleanStatesAtOwnerTurnStart") && r.RequiredBool("expireBooleanStatesAtOwnerTurnStart")) : null
        };
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class ZhuLingZhanyiChooseCategoryHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiChooseCategory;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhuLingProgramHost>().ZhuLingZhanyiChooseCategory(f, e);
}

// 战意 equipment punish belongs to each actual equipment-zone entry, including
// placements by other skills that never declare a card Use.
internal sealed class ZhuLingZhanyiEquipmentPunishDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhuLingZhanyiEquipmentPunishHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOtherOwnedCardDiscard,
        static (effect, context) => context.ChooseOtherOwnedCardDiscard(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
    [
        new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained),
        new RequireContext(ProgramContextCapability.MovementSource)
    ];

    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish)) return;
        if (trigger.Effects.Count != 1 || trigger.Window != SkillProgramTriggerWindow.CardsGained ||
            trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional ||
            trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerCard ||
            !trigger.DestinationZones.SequenceEqual([CardZoneKind.Equipment]))
            throw new InvalidOperationException(path + ": Zhanyi equipment punishment requires one optional owner per-card equipment entry.");
    }
}

public sealed class ZhuLingZhanyiEquipmentPunishHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhuLingZhanyiEquipmentPunish;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IZhuLingProgramHost>().ZhuLingZhanyiEquipmentPunish(f, e);
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
