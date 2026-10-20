namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasSunHaoCanshiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard);
}
