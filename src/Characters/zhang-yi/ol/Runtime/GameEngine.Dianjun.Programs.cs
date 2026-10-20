namespace CardGame.Core;

public sealed partial class GameEngine
{

    // The central typed phase scheduler restores this exact program and its
    // original DeferredTurnEndFrame before calling this return.
    private void ResumeZhangYiDianjunExtraPlay(long programFrameId)
    {
        var f = GetActiveProgramFrame(programFrameId);
        if (f.ZhangYi is not { Stage: ZhangYiStage.DianjunExtraPlay } r || r.ActualTurnNumber != _turnNumber || _phase != TurnPhase.Finished)
            throw new InvalidOperationException("Dianjun received a foreign extra-phase return.");
        ReplaceRuntimeTop(f with { ZhangYi = r with { Stage = ZhangYiStage.DianjunReturned } }); AdvanceRuntimeProgram(programFrameId);
    }
}
