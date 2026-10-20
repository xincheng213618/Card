namespace CardGame.Core;

internal interface ITianYuProgramHost
{
    SkillProgramStepOutcome SaodiExpandTargets(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuitaoMarkTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuitaoRevoke(ProgramSkillFrame frame, SkillProgramEffect effect);
}
