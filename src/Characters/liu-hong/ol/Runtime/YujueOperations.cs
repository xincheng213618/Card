namespace CardGame.Core;

// 鬻爵: the optional play-phase invocation spends one chosen equipment slot,
// takes a hand card from one chosen other character and grants that character
// 执笏 until the owner's next turn. One op drives the whole chained prompt; the
// estimate prices the owner's net card gain through the shared GainCards path.
internal sealed class YujueResolveDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YujueResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new YujueResolveHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireActivationEntry()];

    internal static void ValidateComposition(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, int selectedCardCount, bool initialSelectedTarget,
        int initialTargetSetMaximum, int? activationMinimumCards)
    {
        if (!effects.Any(effect => effect.Op == SkillProgramEffectOp.YujueResolve)) return;
        if (window is not null || selectedCardCount != 0 || activationMinimumCards != 0 ||
            initialSelectedTarget || initialTargetSetMaximum != 0 ||
            effects is not [{ Op: SkillProgramEffectOp.YujueResolve }])
            throw new InvalidOperationException($"Invalid skill program at {path}: Yujue requires one standalone zero-card zero-target activation.");
    }
}

public sealed class YujueResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YujueResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuHongProgramHost>().YujueResolve(f, e);
}
