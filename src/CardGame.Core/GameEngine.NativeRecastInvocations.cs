namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksNativeRecastInvocations =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouHuangYueyingBingcai) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveChanshuangSimultaneousChoices) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveChanshuangEnding);

    private NativeRecastInvocationDraft? CaptureNativeRecastInvocation(int initiatorSeat, int executorSeat,
        IReadOnlyList<int> originalPrimaryIds, long? parentFrameId = null, int? instructionIndex = null,
        int? participantCursor = null, CardMoveReason? reason = null)
    {
        if (!TracksNativeRecastInvocations || !_setupComplete || _turnNumber < 1 ||
            !IsValidPlayerSeat(_turnProgression.OwnerSeat) || originalPrimaryIds.Count == 0) return null;
        if (!IsValidPlayerSeat(initiatorSeat) || !IsValidPlayerSeat(executorSeat) ||
            originalPrimaryIds.Distinct().Count() != originalPrimaryIds.Count)
            throw new InvalidOperationException("A native recast needs one real participant and distinct original primary materials.");
        var materials = originalPrimaryIds.Select(id =>
        {
            var from = _cardZones.GetLocation(id);
            if (from.OwnerSeat != executorSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain or CardZoneKind.PublicDeferredPile or CardZoneKind.PublicPersistentPile))
                throw new InvalidOperationException("A native recast must capture its participant's actual owned primary materials before payment.");
            var card = _cardZones.CardsAt(from).Single(c => c.Id == id);
            return new NativeRecastMaterial(id, card.Kind, from, card.IsGeneralWeapon);
        }).ToArray();
        return new NativeRecastInvocationDraft
        {
            InvocationId = ++_resolutionSequence, TurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _turnProgression.OwnerSeat, InitiatorSeat = initiatorSeat,
            ExecutorSeat = executorSeat, SequenceBefore = _movementSequence,
            ParentFrameId = parentFrameId ?? _resolutionStack.LastOrDefault()?.Id,
            InstructionIndex = instructionIndex, ParticipantCursor = participantCursor,
            Reason = reason ?? CardMoveReasons.RecastDiscard, Materials = materials
        };
    }

    private void CompleteNativeRecastInvocation(NativeRecastInvocationDraft? draft, int actualDrawCount)
    {
        if (draft is null) return;
        if (!TracksNativeRecastInvocations || draft.InvocationId <= 0 || draft.InvocationId > _resolutionSequence ||
            draft.Materials.Count == 0 || actualDrawCount < 0 || actualDrawCount > draft.Materials.Count ||
            draft.Materials.Select(m => m.CardId).Distinct().Count() != draft.Materials.Count ||
            ProgramEventHistory<NativeRecastInvocationRecordedEvent>().Any(e => e.InvocationId == draft.InvocationId))
            throw new InvalidOperationException("A native recast must complete its whole original participant group exactly once.");
        foreach (var material in draft.Materials)
        {
            var paid = _cardMovements.Where(m => m.Sequence > draft.SequenceBefore && m.Sequence <= _movementSequence &&
                m.CardId == material.CardId && m.CardKind == material.PrintedKind && m.From == material.From &&
                m.Reason == draft.Reason && (m.To == CardLocation.DiscardPile ||
                    (material.IsGeneralWeapon || IsGuanduThunderChariotDeparture(GetAdvancedCard(material.CardId), material.From, m.To) || IsEphemeralCardDestroyedMaterial(m)) &&
                    m.To == CardLocation.OutsideGame)).ToArray();
            // A child may legitimately recover and recast this same entity again.
            // The first matching payment after capture belongs to this producer;
            // later payments never increase this invocation's primary count.
            if (paid.Length == 0)
                throw new InvalidOperationException("A native recast completion lost an exact original primary payment; spill and later movements are not recast materials.");
        }
        AdvanceEventRulesAndQueueFact(new NativeRecastInvocationRecordedEvent(draft.InvocationId,
            draft.TurnNumber, draft.ActualTurnOwnerSeat, draft.InitiatorSeat, draft.ExecutorSeat,
            draft.Materials.Count, draft.Materials.Count, actualDrawCount, draft.ParentFrameId,
            draft.InstructionIndex, draft.ParticipantCursor, draft.SequenceBefore, _movementSequence));
    }

    private ProgramSkillFrame BeginFrameNativeRecastInvocation(ProgramSkillFrame frame, int executorSeat,
        IReadOnlyList<int> originalPrimaryIds, int? participantCursor = null)
    {
        if (!TracksNativeRecastInvocations) return frame;
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.NativeRecastInvocation is not null)
            throw new InvalidOperationException("A program cannot overlap two unfinished recasts on one owning frame.");
        var draft = CaptureNativeRecastInvocation(frame.OwnerSeat, executorSeat, originalPrimaryIds,
            frame.Id, frame.InstructionIndex, participantCursor);
        if (draft is not null) ReplaceRuntimeTop(frame = frame with { NativeRecastInvocation = draft });
        return frame;
    }

    private ProgramSkillFrame CompleteFrameNativeRecastInvocation(ProgramSkillFrame frame, int actualDrawCount)
    {
        if (!TracksNativeRecastInvocations) return frame;
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.NativeRecastInvocation is not { } draft) return frame;
        if (draft.ParentFrameId != frame.Id || draft.InstructionIndex != frame.InstructionIndex)
            throw new InvalidOperationException("A paid native recast changed its owning program instruction.");
        ReplaceRuntimeTop(frame = frame with { NativeRecastInvocation = null });
        CompleteNativeRecastInvocation(draft, actualDrawCount);
        return GetActiveProgramFrame(frame.Id);
    }
}
