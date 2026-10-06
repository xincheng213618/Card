namespace CardGame.Core;

internal interface IGaoLanProgramHost
{
    void XizhenResponseBenefit(ProgramSkillFrame frame);
}

// 袭阵's lingering benefit: while the duel target carries the XiZhen marker, a
// response to the owner's play-phase card recovers the target and draws the
// owner one card if the target is still wounded afterwards, otherwise two.
internal sealed class XizhenResponseBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.XiZhenResponseBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new XizhenResponseBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("The XiZhen benefit requires the owner.");
        return new(Op, target, 0, r.Condition());
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}

public sealed class XizhenResponseBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.XiZhenResponseBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    {
        ((IGaoLanProgramHost)h).XizhenResponseBenefit(f);
        return SkillProgramStepOutcome.Continue;
    }
}
