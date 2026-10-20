using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain)]
    private void ReturnBiriDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.ZhaoZhong!;
        if (!r.Paid || r.Stage != ZhaoZhongStage.Drawing || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1) throw new InvalidOperationException("Biri returned to another issued native draw.");
        ReplaceRuntimeTop(f = f with { ZhaoZhong = r with { Stage = ZhaoZhongStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BiriDrawReturnedEvent(f.Id, 1, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId)); ContinueZhaoZhong(f.Id);
    }
}
