namespace CardGame.Core;

internal interface IGuanYuVariantHost
{
    SkillProgramStepOutcome ExecuteGuanYuVariant(ProgramSkillFrame frame, SkillProgramEffectOp op);
}

internal sealed class GuanYuVariantHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IGuanYuVariantHost>().ExecuteGuanYuVariant(frame, op);
}

internal abstract class GuanYuVariantDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new GuanYuVariantHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy => Op switch
    {
        SkillProgramEffectOp.ArmPhaseSlashFocus => new(ProgramOperationAiSemantic.GainCards,
            static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, effect.Condition))),
        SkillProgramEffectOp.HoldRoundFocusCards => new(ProgramOperationAiSemantic.HoldTargetCards,
            static (effect, context) => context.HoldTargetCards(effect)),
        _ => new(ProgramOperationAiSemantic.GainCards, static (_, _) => { })
    };
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.ArmPhaseSlashFocus => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)],
        SkillProgramEffectOp.HoldRoundFocusCards => [new ReadSelectedTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)],
        SkillProgramEffectOp.SpareFirstLethalTarget => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)],
        SkillProgramEffectOp.KillRoundFocusSlashVictim => [new RequireTriggerWindow(SkillProgramTriggerWindow.DamageAppliedBeforeDying)],
        SkillProgramEffectOp.GrantExclusiveRende or SkillProgramEffectOp.GrantExclusiveQingnang or SkillProgramEffectOp.GrantExclusiveLongyin =>
            [new ReadTargetSet(0, 1), new ReleaseTargetSet(), new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)],
        SkillProgramEffectOp.RecordOpeningTurnUse => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]
    };
}
