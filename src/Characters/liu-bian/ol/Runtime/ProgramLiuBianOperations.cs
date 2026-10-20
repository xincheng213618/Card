namespace CardGame.Core;

internal interface ILiuBianProgramHost
{
    SkillProgramStepOutcome ShiYuanTargetDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome DuShiGrantSkill(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YuWeiMarkActiveTurn(ProgramSkillFrame frame, SkillProgramEffect effect);
}
