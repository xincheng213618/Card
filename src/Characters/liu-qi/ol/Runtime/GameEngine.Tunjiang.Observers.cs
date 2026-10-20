namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasTunjiangCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawForUnskippedUntargetedPlay);
}
