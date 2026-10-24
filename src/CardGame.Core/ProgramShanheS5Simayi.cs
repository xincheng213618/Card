namespace CardGame.Core;

internal interface IShanheS5BaiyinHost
{ SkillProgramStepOutcome GrantShanheS5Baiyin(ProgramSkillFrame frame); }

internal sealed class GrantShanheS5BaiyinDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantShanheS5Baiyin;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantShanheS5BaiyinHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills,
        static (_, context) => context.PublicControlValue(1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];

    [ProgramCatalogValidation(59000)]
    internal static void ValidateOperation(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantShanheS5Baiyin)))
            throw new InvalidOperationException(path + ": S5 Baiyin grants only from its original preparation trigger.");
        foreach (var trigger in program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantShanheS5Baiyin)))
            if (program.Id != "shanhe:s5-baiyin-14193" || trigger is not
                { Id: "awakening", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                    Subject: SkillProgramTriggerSubject.Owner, Optional: false, UsageScope: SkillUsageScope.Game, UsageLimit: 1,
                    Effects: [{ Op: SkillProgramEffectOp.GrantShanheS5Baiyin, Target: SkillProgramEffectTarget.Owner,
                        Condition.Kind: SkillProgramConditionKind.Always }] })
                throw new InvalidOperationException(path + ": the S5 Baiyin operation requires one original owner preparation awakening grant.");
    }
}

public sealed class GrantShanheS5BaiyinHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantShanheS5Baiyin;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShanheS5BaiyinHost>().GrantShanheS5Baiyin(frame);
}
