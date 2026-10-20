namespace CardGame.Core;
internal sealed class ZaowangResolveDescriptor : DongZhaoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ZaowangResolve;
    public override ISkillProgramEffectHandler Handler { get; } = new ZaowangResolveHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
public sealed class ZaowangResolveHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ZaowangResolve;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) => h.GetCapability<IDongZhaoProgramHost>().ZaowangResolve(f);
}
