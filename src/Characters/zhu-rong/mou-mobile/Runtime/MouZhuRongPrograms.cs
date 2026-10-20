namespace CardGame.Core;

internal interface IMouZhuRongHost
{
    void RecordLieren(ProgramSkillFrame frame);
    SkillProgramStepOutcome SelectLierenTarget(ProgramSkillFrame frame);
    SkillProgramStepOutcome GiftAssault(ProgramSkillFrame frame);
}
internal sealed class MouZhuRongHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    {
        var host = h.GetCapability<IMouZhuRongHost>();
        if (op == SkillProgramEffectOp.RecordMouZhuRongLieren) { host.RecordLieren(f); return SkillProgramStepOutcome.Continue; }
        return op == SkillProgramEffectOp.SelectMouZhuRongLierenTarget ? host.SelectLierenTarget(f) : host.GiftAssault(f);
    }
}
