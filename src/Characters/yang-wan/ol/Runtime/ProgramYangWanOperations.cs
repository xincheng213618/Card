namespace CardGame.Core;

internal interface IYangWanProgramHost
{
    SkillProgramStepOutcome YouyanGainSuitCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuihuanArm(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhuihuanRetaliate(ProgramSkillFrame frame, SkillProgramEffect effect);
}
