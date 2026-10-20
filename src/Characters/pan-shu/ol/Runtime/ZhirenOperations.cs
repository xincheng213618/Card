namespace CardGame.Core;

// 织纴 tier 1: the used card's printed name length X views the top X cards and
// rearranges them over the draw-pile top and bottom (classic top-reorder shape).
internal sealed class ZhirenReorderTopByLengthDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenReorderTopByLength;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhirenReorderTopByLengthHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) => context.Move(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireContext(ProgramContextCapability.CardAction)];
}

public sealed class ZhirenReorderTopByLengthHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenReorderTopByLength;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IPanShuProgramHost>().ZhirenReorderTopByLength(f, e);
}

// 织纴 tiers 2-4: with X >= 2 discard at most one field equipment and one field
// delayed trick, with X >= 3 recover one HP, and with X >= 4 draw three cards.
internal sealed class ZhirenResolveFieldTiersDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenResolveFieldTiers;
    public override ISkillProgramEffectHandler Handler { get; } = new ZhirenResolveFieldTiersHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireContext(ProgramContextCapability.CardAction)];
}

public sealed class ZhirenResolveFieldTiersHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZhirenResolveFieldTiers;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IPanShuProgramHost>().ZhirenResolveFieldTiers(f, e);
}
