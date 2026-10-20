namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int BingzhongCurrentX() => checked(1 + _players.Count(p => p.IsAlive && GetHand(p).Count == 0));
    private SkillProgramStepOutcome BeginBingzhongDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:bingzhong" || f.TriggerId != "actual-damage-dynamic-x" || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: not null } c || c.TargetSeat != f.OwnerSeat)
            throw new InvalidOperationException("Bingzhong requires its true whole positive damage occurrence.");
        var r = NewYangFuReceipt(f, SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw, YangFuStage.DrawChildren, f.OwnerSeat, BingzhongCurrentX());
        StartYangFu(f, r); f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var count = DrawCards(_players[f.OwnerSeat], r.X, true, new(BingzhongDrawReason)).Count;
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { YangFu = f.YangFu! with { After = _movementSequence, DrawCount = count } });
        AdvanceEventRulesAndQueueFact(new BingzhongDrawPaidEvent(f.Id, r.X, count, r.Before, _movementSequence));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
}
