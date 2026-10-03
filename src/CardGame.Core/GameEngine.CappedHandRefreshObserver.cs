namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only an exact paid 2900 instruction and its actual movement can carry
    // observers above a suspended damage cursor. Source lifetime is checked
    // when the paid operation resumes, after all lawful children have returned.
    private ProgramSkillFrame? CappedHandRefreshObserverRoot(long damageWindowId)
    {
        for (var index = 1; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root ||
                root.CappedHandRefresh is not { Stage: ProgramCappedHandRefreshStage.Drawing or ProgramCappedHandRefreshStage.Discarding } draft ||
                root.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
                context.ParentFrameId != damageWindowId ||
                root.SelectedTargetSeats is not [var target] || target != draft.TargetSeat ||
                root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != target ||
                _resolutionStack[index - 1] is not DamageTriggerWindowFrame damage || damage.Id != damageWindowId ||
                context.DamageFrameId != damage.ParentFrameId || context.TargetSeat != damage.TargetSeat || context.OwnerSeat != root.OwnerSeat ||
                damage.CandidateIndex < 0 || damage.CandidateIndex >= damage.Candidates.Count ||
                !MountObserverCandidateMatches(root, damage.Candidates[damage.CandidateIndex].ToProgramCandidate()) ||
                _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame movement ||
                movement.Batch.Id != movement.Id || movement.Batch.ParentFrameId != root.Id ||
                movement.Batch.AwaitingProgramFrameId is { } awaited && awaited != root.Id ||
                movement.Batch.OriginSkillId != root.SkillId || movement.Batch.OriginSkillInstanceId != root.SkillInstanceId ||
                movement.Batch.OriginOwnerSeat != root.OwnerSeat || movement.Batch.Movements.Count == 0 ||
                movement.Batch.Movements.Any(record => !_cardMovements.Contains(record))) continue;
            var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
            if (root.InstructionIndex < 1 || root.InstructionIndex > plan.Instructions.Count ||
                plan.GetPausedInstruction(root.InstructionIndex).Effect is not
                    { Op: SkillProgramEffectOp.DrawThenDiscardHandToMaximumHp, Target: SkillProgramEffectTarget.SelectedTarget } paid ||
                draft.Maximum < 0 || draft.Maximum > paid.Amount) continue;
            var actual = movement.Batch.Movements;
            var exact = draft.Stage == ProgramCappedHandRefreshStage.Drawing
                ? draft.DiscardCount == 0 && draft.SelectedCardIds.Count == 0 && draft.CandidateCardIds.Count == 0 &&
                  actual.Count == 1 && actual[0].From == CardLocation.DrawPile && actual[0].To == CardLocation.Hand(target) &&
                  actual[0].Reason.Value == "program.capped-hand-refresh.draw" &&
                  _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramCappedHandRefreshDrawnEvent>()
                    .Any(e => e.FrameId == root.Id && e.OwnerSeat == root.OwnerSeat && e.TargetSeat == target &&
                        e.Maximum == draft.Maximum && e.DrawCount > 0)
                : draft.DiscardCount > 0 && draft.SelectedCardIds.Count == draft.DiscardCount &&
                  actual.Count == draft.DiscardCount && actual.Select(m => m.CardId).Order()
                    .SequenceEqual(draft.SelectedCardIds.Order()) &&
                  actual.All(m => m.From == CardLocation.Hand(target) && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "program.capped-hand-refresh.discard");
            if (!exact) continue;
            var aligned = true;
            for (var child = index + 1; child < _resolutionStack.Count; child++)
                if (!PaidMountObserverEdge(child)) { aligned = false; break; }
            if (aligned) return root;
        }
        return null;
    }

    private bool HasCappedHandRefreshObserver(long damageWindowId) => CappedHandRefreshObserverRoot(damageWindowId) is not null;

    private bool IsCappedHandRefreshProgramDying() =>
        ActiveDamageTrigger is { } damage && ActiveDying is { ResumesProgramSkill: true } dying &&
        CappedHandRefreshObserverRoot(damage.Id) is not null &&
        _resolutionStack.OfType<DyingFrame>().Any(frame => frame.Id == dying.FrameId && frame.ParentFrameId == dying.ParentFrameId);
}
