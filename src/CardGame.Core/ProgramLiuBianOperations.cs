namespace CardGame.Core;

internal interface ILiuBianProgramHost
{
    SkillProgramStepOutcome ShiYuanTargetDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome DuShiGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YuWeiMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 诗怨: becoming the target of another character's card offers a draw of three,
// two or one cards by that character's health compared to the owner's; each
// option is limited per turn and 余威 doubles the limit on other Qun turns.
internal sealed class ShiYuanTargetDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ShiYuanTargetDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new ShiYuanTargetDrawHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 2, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}

public sealed class ShiYuanTargetDrawHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ShiYuanTargetDraw;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuBianProgramHost)host).ShiYuanTargetDraw(f, e);
}

// 毒逝: on the owner's death a chosen other character acquires 毒逝 itself,
// carrying both the death grant and the dying self-rescue limit onward.
internal sealed class DuShiGrantSkillDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DuShiGrantSkill;
    public override ISkillProgramEffectHandler Handler { get; } = new DuShiGrantSkillHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (effect, context) => context.GrantSkills(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget()];
}

public sealed class DuShiGrantSkillHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DuShiGrantSkill;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuBianProgramHost)host).DuShiGrantSkill(f, e);
}

// 余威: commits the evidence that this play phase belongs to another
// Qun-faction character, which is the ledger 诗怨 reads for its doubled
// per-option limit; the play-phase boundary is the earliest shared window that
// covers every use made during that character's turn.
internal sealed class YuWeiMarkActiveTurnDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuWeiMarkActiveTurn;
    public override ISkillProgramEffectHandler Handler { get; } = new YuWeiMarkActiveTurnHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class YuWeiMarkActiveTurnHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuWeiMarkActiveTurn;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((ILiuBianProgramHost)host).YuWeiMarkActiveTurn(f, e);
}
