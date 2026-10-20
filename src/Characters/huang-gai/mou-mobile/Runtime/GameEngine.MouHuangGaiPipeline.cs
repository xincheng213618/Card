namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramPipelineHook(ProgramPipelinePhase.CaptureVirtualDuelLegacy, 4)]
    private void IssueMouZhaJiangLegacyDuelPipeline(ProgramSkillFrame frame, int sourceSeat, int targetSeat) =>
        IssueMouZhaJiangLegacyDuel(frame, sourceSeat);
}
