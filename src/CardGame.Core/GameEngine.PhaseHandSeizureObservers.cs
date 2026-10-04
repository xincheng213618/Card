namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PhaseHandSeizureFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if (root.PhaseHandSeizure is not null && !ValidPhaseHandSeizureReceipt(root) ||
            root.PhaseHandDebtReturn is not null && !ValidPhaseHandDebtReturnReceipt(root)) return false;
        if (child is ProgramLifecycleTriggerWindowFrame face)
            return root.PhaseHandSeizure is { Stage: PhaseHandSeizureStage.PaymentChildren or PhaseHandSeizureStage.FlipChildren } receipt &&
                face.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && face.CharacterStateContinuation == CharacterStateContinuation.Program &&
                face.ResumeProgramFrameId == root.Id && face.OwnerSeat == root.OwnerSeat &&
                face.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp &&
                (receipt.Stage != PhaseHandSeizureStage.FlipChildren || face.Window != SkillProgramTriggerWindow.CharacterTurnedOver ||
                 face.Facts.OwnerIsFaceDown == !receipt.WasFaceDown);
        if (root.PendingMovementContinuation is not { } pending || !IsPhaseHandSeizureMovement(root, GetPausedPrivateOfferEffect(root), pending)) return false;
        long before, after; string reason; IReadOnlyList<int> ids;
        if (root.PhaseHandSeizure is { Stage: PhaseHandSeizureStage.PaymentChildren } cost)
        { before = cost.CostBefore; after = cost.CostAfter; reason = PhaseHandSeizureReason(root, "payment"); ids = [cost.CostCardId]; }
        else if (root.PhaseHandSeizure is { Stage: PhaseHandSeizureStage.TakeChildren } take && take.TargetSeat != root.OwnerSeat)
        { before = take.TakeBefore; after = take.TakeAfter; reason = PhaseHandSeizureReason(root, "take"); ids = take.TakenCardIds; }
        else if (root.PhaseHandDebtReturn is { Stage: PhaseHandDebtReturnStage.MovementChildren } gift)
        { before = gift.SequenceBefore; after = gift.SequenceAfter; reason = PhaseHandDebtReturnReason(root);
            ids = Array.AsReadOnly(gift.CardIds.Where((id, index) => gift.Locations[index] != CardLocation.Hand(gift.TargetSeat)).ToArray());
            if (ids.Count == 0) return false; }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId == root.Id &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > before && m.Sequence <= after && ids.Contains(m.CardId) && m.Reason.Value == reason ||
                     m.Reason == CardMoveReasons.WoodenOxGrainDiscard && m.From == CardLocation.WoodenOxGrain(root.OwnerSeat) && m.To == CardLocation.DiscardPile &&
                     _cardMovements.Any(cost => cost.Sequence > before && cost.Sequence <= after && ids.Contains(cost.CardId) &&
                         cost.CardKind == CardKind.WoodenOx && cost.From == CardLocation.Equipment(root.OwnerSeat) && cost.Reason.Value == reason)));
        var lion = _cardMovements.Any(m => m.Sequence > before && m.Sequence <= after && ids.Contains(m.CardId) &&
            m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(root.OwnerSeat) && m.Reason.Value == reason);
        if (!lion) return false;
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.ResumeFrameId == root.Id && hp.Change.ParentFrameId == root.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.SourceSeat == root.OwnerSeat && hp.Change.TargetSeat == root.OwnerSeat && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame replacement && RecoveryReplacementFrameRidesOn(replacement, root) &&
            replacement.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replacement.Attempt.Completion.MoveReason?.Value == reason && replacement.Attempt.SourceSeat == root.OwnerSeat &&
            replacement.Attempt.TargetSeat == root.OwnerSeat && replacement.Attempt.Amount == 1;
    }
    // The mature structural edge checks candidates, requested facts, movement occurrences, actual uses and typed returns.
    // It is deliberately used directly: no ActiveDying query belongs in this structural prefix.
    private ProgramSkillFrame? PhaseHandSeizureObserverRoot()
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            if (_resolutionStack[index] is not ProgramSkillFrame root || !PhaseHandSeizureFirstChild(root, _resolutionStack[index + 1])) continue;
            var exact = true;
            for (var child = index + 2; child < _resolutionStack.Count; child++)
                if (!DyingSuitsStructuralEdge(_resolutionStack[child - 1], _resolutionStack[child])) { exact = false; break; }
            if (exact) return root;
        }
        return null;
    }
    private bool HasPhaseHandSeizureDamageObserver(long windowId) => PhaseHandSeizureObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == windowId && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsPhaseHandSeizureProgramDying() => ActiveDying is { } dying && PhaseHandSeizureObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsPhaseHandSeizureNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (frame.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != frame.Id || frame.InstructionIndex < 1 ||
            PhaseHandSeizureObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        return !sourceLess && effect.Op == SkillProgramEffectOp.Damage && amount == effect.Amount && source == effect.ActorReference &&
            nature == effect.DamageNature && target == (effect.TargetReference is { } reference
                ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
}
