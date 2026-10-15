namespace CardGame.Core;
internal interface IParticipantPreventionHost
{ SkillProgramStepOutcome PreventParticipantDamageDrawActualSource(ProgramSkillFrame frame); }
internal sealed class PreventParticipantDamageDrawActualSourceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventParticipantDamageDrawActualSource;
    public override ISkillProgramEffectHandler Handler { get; } = new ParticipantPreventionHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.PreventCurrentDamage,
        static(e,c)=>c.PreventCurrentDamage(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class ParticipantPreventionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PreventParticipantDamageDrawActualSource;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h) =>
        ((IParticipantPreventionHost)h).PreventParticipantDamageDrawActualSource(f);
}
internal static class ParticipantPreventionComposition
{
    internal static void ValidateTrigger(string path,SkillProgramTrigger t)
    {
        if(!t.Effects.Any(e=>e.Op==SkillProgramEffectOp.PreventParticipantDamageDrawActualSource))return;
        if(t.Effects is not [{Op:SkillProgramEffectOp.PreventParticipantDamageDrawActualSource}] || !t.Optional ||
            t.Window!=SkillProgramTriggerWindow.BeforeDamageApplied || t.Subject is not (SkillProgramTriggerSubject.DamageSource or SkillProgramTriggerSubject.DamageTarget) ||
            t.UsageScope!=SkillUsageScope.Turn || t.UsageLimit!=1 || string.IsNullOrWhiteSpace(t.NamedUsageGroup))
            throw new InvalidOperationException(path+": participant prevention needs the exact damage participant and shared once-per-turn optional boundary.");
    }
}
