namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureVirtualDuelLegacy, 2)]
    private void CaptureMoLuBuLegacyDuelPipeline(ProgramSkillFrame frame, int sourceSeat, int targetSeat) =>
        CaptureMoLuBuLegacyDuel(frame, sourceSeat);
}
