namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuLuXunZhangcaiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuXunZhangcai);

    [ProgramCapability]
    private sealed class WuLuXunZhangcaiCapability(GameEngine engine) : IWuLuXunZhangcaiHost
    {
        public SkillProgramStepOutcome Zhangcai(ProgramSkillFrame frame) => engine.BeginWuLuXunZhangcai(frame);
        public SkillProgramStepOutcome Ruxian(ProgramSkillFrame frame) => engine.ActivateWuLuXunRuxian(frame);
    }
}
