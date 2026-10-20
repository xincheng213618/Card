namespace CardGame.Core;

internal interface ILiuHongProgramHost
{
    SkillProgramStepOutcome YujueResolve(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhihuExpire(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome TuxingArmGameDamage(ProgramSkillFrame frame, SkillProgramEffect effect);
}
