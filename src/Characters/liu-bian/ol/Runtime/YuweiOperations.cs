namespace CardGame.Core;

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
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}

public sealed class YuWeiMarkActiveTurnHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.YuWeiMarkActiveTurn;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuBianProgramHost>().YuWeiMarkActiveTurn(f, e);
}
