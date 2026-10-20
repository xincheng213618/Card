namespace CardGame.Core;

internal interface IZhouFangProgramHost
{
    SkillProgramStepOutcome DuanfaDiscardAndDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YoudiBaitDiscard(ProgramSkillFrame frame, SkillProgramEffect effect);
}
