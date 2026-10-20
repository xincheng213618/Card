namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasTangTangZexing => _contentRegistry?.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveZexingShownCardChain) == true;
}
