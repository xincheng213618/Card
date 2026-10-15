namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Issued receipts can survive different invalidations. Keep each permission
    // explicit and leave the receipt, stage and ownership checks in its mechanism.
    private bool CanContinueProgramAfterOwnerDeath(ProgramSkillFrame frame) =>
        RuntimeHooks.CanContinue(ProgramRuntimePhase.AfterOwnerDeath, frame);

    private bool CanContinueProgramAfterSkillLoss(ProgramSkillFrame frame) =>
        RuntimeHooks.CanContinue(ProgramRuntimePhase.AfterSkillLoss, frame);

    private sealed partial class ProgramSkillHost
    {
        public bool CanContinueAfterOwnerDeath(ProgramSkillFrame frame) =>
            engine.CanContinueProgramAfterOwnerDeath(frame);

        public bool CanContinueAfterSkillLoss(ProgramSkillFrame frame) =>
            engine.CanContinueProgramAfterSkillLoss(frame);
    }
}
