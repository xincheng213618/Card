namespace CardGame.Core;
public sealed partial class GameEngine
{
    private SkillProgramStepOutcome BeginSuyi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:suyi" || f.TriggerId is not null || f.WindowContext is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Suyi requires its actual one-other-target native Play activation.");
        StartYangFu(f, NewYangFuReceipt(f, SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, YangFuStage.EquipmentUse, target, 0));
        f = GetActiveProgramFrame(f.Id);
        var outcome = UseProgramRandomDeckEquipment(f, target, SuyiEquipmentBind);
        if (outcome == SkillProgramStepOutcome.Continue)
        {
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { YangFu = f.YangFu! with { Stage = YangFuStage.EquipmentChildren }, PendingMovementContinuation = new(target, 0, null) });
            AdvanceEventRulesAndQueueFact(new SuyiNoEquipmentEvent(f.Id, target)); AdvanceRuntimeProgram(f.Id);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }
}
