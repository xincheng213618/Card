namespace CardGame.Core;

internal interface ITengFangLanProgramHost
{
    SkillProgramStepOutcome LuochongResolve(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome AichenRemoveOption(ProgramSkillFrame frame, SkillProgramEffect effect);
}
