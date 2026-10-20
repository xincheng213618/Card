namespace CardGame.Core;

internal interface ITangJiProgramHost
{
    SkillProgramStepOutcome KanggeChooseTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome KanggeGainDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome KanggeHealVictim(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome KanggeDeathPrice(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome JieliePreventAndGift(ProgramSkillFrame frame, SkillProgramEffect effect);
}
