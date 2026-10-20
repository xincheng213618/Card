namespace CardGame.Core;

public sealed partial class GameEngine
{


    [ProgramCapability]
    private sealed class ZhangChangPuProgramCapability(GameEngine engine) : IZhangChangPuProgramHost
    {
        public void YanjiaoRevealTopCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanjiaoRevealProgramTopCards(frame, effect);
        public SkillProgramStepOutcome YanjiaoSplitRevealedCards(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanjiaoSplitRevealedCards(frame, effect);
        public void ShenShenDrawAndArmBonus(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ShenShenDrawAndArmBonus(frame, effect);
    }
}
