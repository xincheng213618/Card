namespace CardGame.Core;

internal interface IPanShuProgramHost
{
    SkillProgramStepOutcome ZhirenReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZhirenResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YanerResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect);
}
