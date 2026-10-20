namespace CardGame.Core;
public sealed partial class GameEngine
{
    private (long Base,int Round,int Count) _miuyanQualificationDependencies;
    private long _miuyanQualificationStamp;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27210)]
    private bool QualifyWangGuan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>t.Effects is not[{Op:SkillProgramEffectOp.ResolveMiuyanCompletedUse}]||CanRunIssuedMiuyanCandidate(c,context);
}
