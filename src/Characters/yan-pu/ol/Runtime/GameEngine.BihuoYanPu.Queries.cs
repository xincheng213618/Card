namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsYanPuDyingExitContext(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.DrawDyingExitRoundDistance }] &&
        context.Window == SkillProgramTriggerWindow.DyingExited && context.OwnerSeat == c.OwnerSeat &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is
            { Window: SkillProgramTriggerWindow.DyingExited, Continuation: ProgramLifecycleContinuation.ResumeYanPuDyingExit, YanPuDyingExit: { } ret } parent &&
        ret.Round == _roundNumber && ret.Turn == _turnNumber && ret.TurnOwner == _currentSeat && parent.OwnerSeat == ret.VictimSeat &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c &&
        context.SourceSeat == ret.VictimSeat && context.TargetSeat == ret.VictimSeat && _players[ret.VictimSeat].IsAlive && _players[ret.VictimSeat].Hp > 0 &&
        _resolutionStack.FindIndex(w => w.Id == parent.Id) is var index && index > 0 && _resolutionStack[index - 1].Id == ret.DyingParentFrameId &&
        ProgramEventHistory<DyingResolvedEvent>().Count(e => e.ResolutionId == ret.DyingFrameId && e.VictimSeat == ret.VictimSeat && e.Survived) == 1 &&
        !ProgramEventHistory<YanPuStartedEvent>().Any(e => e.Stage == YanPuStage.ExitDrawChildren && e.Source.OwnerSeat == c.OwnerSeat && e.Source.SkillId == c.SkillId);
}
