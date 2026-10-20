namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasDenglouCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ViewDenglouTopAndUseBasics);
}
