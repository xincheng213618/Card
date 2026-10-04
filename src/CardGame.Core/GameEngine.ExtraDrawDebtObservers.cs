namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? ExtraDrawDebtObserverRoot()
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent ||
                parent.Window != SkillProgramTriggerWindow.DrawPhaseStarting ||
                _resolutionStack[index] is not ProgramSkillFrame root || root.ExtraDrawReceipt is not { } paid ||
                root.InstructionIndex != 1 || paid.InstructionIndex != 0 || paid.DrawWindowFrameId != parent.Id ||
                paid.ActualTurnNumber != _turnNumber || paid.TurnOwnerSeat != root.OwnerSeat || parent.OwnerSeat != root.OwnerSeat ||
                root.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } context || context.ParentFrameId != parent.Id ||
                parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
                !MountObserverCandidateMatches(root, parent.Candidates[parent.CandidateIndex]) ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != root.OwnerSeat)
                continue;
            var program = _contentRegistry.GetSkill(root.SkillId).Program;
            if (program is null || program.GameplayHash != root.GameplayHash ||
                ProgramInstructionResolver.Default.Resolve(root, program).Instructions is not
                    [{ Op: SkillProgramEffectOp.DrawExtraAndArmTurnDamageUseDebt } op] || op.StateId != paid.StateId) continue;
            AssertTurnDrawDebtReceipts(root, [op]);
            if (_resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame moved ||
                moved.Batch.ParentFrameId != root.Id || moved.Batch.AwaitingProgramFrameId is { } awaiting && awaiting != root.Id ||
                moved.Batch.OriginOwnerSeat != root.OwnerSeat || moved.Batch.OriginSkillId != root.SkillId ||
                moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.Movements.Count == 0 ||
                moved.Batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= paid.MovementSequenceBefore ||
                    m.Sequence > paid.MovementSequenceAfter || m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(root.OwnerSeat) ||
                    m.Reason.Value != "program.extra-draw-debt.draw")) continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
            {
                if (!PreventionDrawObserverEdge(child)) { aligned = false; break; }
                if (_resolutionStack[child] is DyingFrame dying && IsPaidHandRepaymentProgramAlcoholRide(child, dying)) break;
            }
            if (aligned) return root;
        }
        return null;
    }
    private bool IsExtraDrawDebtProgramDying() => ActiveDying is { ResumesProgramSkill: true } dying &&
        ExtraDrawDebtObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
}
