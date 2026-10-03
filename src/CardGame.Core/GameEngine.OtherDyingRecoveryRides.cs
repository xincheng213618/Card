namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Only the exact new other-victim recovery program may extend a DyingEntry
    // through its equipment cost, face-state transition and recovery children.
    private bool IsExactOtherDyingRecoveryRide(long dyingId)
    {
        var index = _resolutionStack.FindLastIndex(f => f is DyingFrame && f.Id == dyingId);
        if (index < 0 || index + 2 >= _resolutionStack.Count ||
            _resolutionStack[index] is not DyingFrame dying ||
            _resolutionStack[index + 1] is not ProgramLifecycleTriggerWindowFrame entry ||
            entry.Window != SkillProgramTriggerWindow.DyingEntering ||
            entry.Continuation != ProgramLifecycleContinuation.ResumeDyingEntry || entry.ResumeDyingFrameId != dying.Id ||
            entry.OwnerSeat != dying.VictimSeat || entry.CandidateIndex < 0 || entry.CandidateIndex >= entry.Candidates.Count ||
            _resolutionStack[index + 2] is not ProgramSkillFrame root ||
            root.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } context ||
            context.ParentFrameId != entry.Id || context.TargetSeat != dying.VictimSeat || root.OwnerSeat == dying.VictimSeat ||
            !MountObserverCandidateMatches(root, entry.Candidates[entry.CandidateIndex])) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(root, _contentRegistry.GetSkill(root.SkillId).Program!);
        if (!plan.Instructions.Any(e => e.Op == SkillProgramEffectOp.RecoverOtherDyingVictimTo) ||
            root.InstructionIndex < 1 || root.InstructionIndex > plan.Instructions.Count) return false;
        var paused = plan.Instructions[root.InstructionIndex - 1];
        if (paused.Op is not (SkillProgramEffectOp.SelectAndMoveOwnedCard or SkillProgramEffectOp.TurnOver or
            SkillProgramEffectOp.RecoverOtherDyingVictimTo)) return false;
        var cost = plan.Instructions.FirstOrDefault(e => e.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard);
        if (cost is not { AwaitMovementTriggers: true, Destination: SkillProgramCardDestination.DiscardPile } ||
            cost.CardOwnerRef?.Kind != ProgramParticipantRef.Owner || cost.ChooserRef?.Kind != ProgramParticipantRef.Owner ||
            !cost.CardCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) || cost.ResultBind is null) return false;
        if (paused.Op != SkillProgramEffectOp.SelectAndMoveOwnedCard)
        {
            var paid = root.CardSetBindings.SingleOrDefault(b => b.Name == cost.ResultBind);
            // Selected-card bindings describe their current destination. The
            // committed movement ledger proves the original owned-card cost.
            if (paid is null || paid.CardIds is not [var card] ||
                paid.SourceLocations is not [var destination] || destination != CardLocation.DiscardPile ||
                !_cardMovements.Any(m => m.CardId == card && m.From.OwnerSeat == root.OwnerSeat &&
                    m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.To == destination &&
                    m.Reason.Value == $"skill-program.{root.SkillId}.{SkillProgramEffectOp.SelectAndMoveOwnedCard}")) return false;
        }
        for (var childIndex = index + 3; childIndex < _resolutionStack.Count; childIndex++)
        {
            var child = _resolutionStack[childIndex]; var parent = _resolutionStack[childIndex - 1];
            if (CharacterTurnedOverFrameRidesOn(child, parent))
            {
                if (parent.Id == root.Id && paused.Op != SkillProgramEffectOp.TurnOver) return false;
                continue;
            }
            if (RecoveryReplacementFrameRidesOn(child, parent))
            {
                if (parent.Id == root.Id && paused.Op is not (SkillProgramEffectOp.SelectAndMoveOwnedCard or
                    SkillProgramEffectOp.RecoverOtherDyingVictimTo)) return false;
                continue;
            }
            if (!PaidMountObserverEdge(childIndex)) return false;
        }
        return true;
    }
}
