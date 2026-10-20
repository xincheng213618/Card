namespace CardGame.Core;

internal interface IZhangChangPuProgramHost
{
    void YanjiaoRevealTopCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome YanjiaoSplitRevealedCards(ProgramSkillFrame frame, SkillProgramEffect effect);
    void ShenShenDrawAndArmBonus(ProgramSkillFrame frame, SkillProgramEffect effect);
}
