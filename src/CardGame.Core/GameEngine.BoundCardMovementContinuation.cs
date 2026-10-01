namespace CardGame.Core;

internal interface IBoundCardMovementContinuationHost
{
    SkillProgramStepOutcome AwaitBoundCardMovements(long frameId, int ownerSeat);
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IBoundCardMovementContinuationHost
    {
        public SkillProgramStepOutcome AwaitBoundCardMovements(long frameId, int ownerSeat) =>
            engine.AwaitProgramBoundCardMovements(frameId, ownerSeat);
    }

    private SkillProgramStepOutcome AwaitProgramBoundCardMovements(long frameId, int ownerSeat)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.OwnerSeat != ownerSeat) throw new InvalidOperationException("The bound movement lost its program owner.");
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(ownerSeat, 0, null) });
        if (TryBeginCardsMovedProgramWindow()) return SkillProgramStepOutcome.AwaitChild;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = null });
        return SkillProgramStepOutcome.Continue;
    }
}
