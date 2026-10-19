namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasLeDiaoChanTanbanCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.InitializeTanbanInitialEntities);
    private static CardConversionSource TanbanSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    // One shared binary mark belongs to each physical entity, not to a holder or
    // grant. Losing/reacquiring a source never restores an old initial mark.
    private bool HasTanbanEntityMark(int cardId)
    {
        if (!HasLeDiaoChanTanbanCapability || cardId <= 0) return false;
        return ProgramEventHistory<TanbanEntityMarkChangedEvent>().LastOrDefault(e => e.CardId == cardId &&
            (ProgramEventHistory<TanbanInitializedEvent>().Any(done => done.FrameId == e.FrameId) ||
             ProgramEventHistory<TanbanHandMarksSwappedEvent>().Any(done => done.FrameId == e.FrameId)))?.Marked == true;
    }

    private bool IsTanbanHandLimitExempt(CharacterState owner, int cardId) =>
        HasLeDiaoChanTanbanCapability && owner.IsAlive && HasRuntimeSkill(owner, "le:tanban") &&
        _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && HasTanbanEntityMark(cardId);

    private ProgramLifecycleTriggerWindowFrame CaptureTanbanInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeDiaoChanTanbanCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.TanbanInitialEntities is not null)
            throw new InvalidOperationException("Tanban's final opening Hand can only be frozen once.");
        var owners = window.Candidates.Where(candidate => GetProgramTrigger(candidate).Effects is
            [{ Op: SkillProgramEffectOp.InitializeTanbanInitialEntities }])
            .Select(candidate => candidate.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Hand(owner));
            if (entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("A Tanban initial entity requires its actual final opening-Hand entry.");
            return new TanbanInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { TanbanInitialEntities = entities };
    }

    private bool IsIssuedTanbanInitialCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        HasLeDiaoChanTanbanCapability && candidate.SkillId == "le:tanban" &&
        candidate.BindingId == "mark-initial-entities" && IsValidPlayerSeat(candidate.OwnerSeat) &&
        _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash == candidate.GameplayHash &&
        GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.InitializeTanbanInitialEntities }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == candidate.OwnerSeat &&
        _phase == TurnPhase.NotStarted &&
        _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.GameStarting, Continuation: ProgramLifecycleContinuation.CompleteGameStarting,
              TanbanInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        parent.Candidates[parent.CandidateIndex] == candidate &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<TanbanInitializedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 29040,
        SkillProgramEffectOp.InitializeTanbanInitialEntities)]
    private bool CanOfferTanbanInitial(ProgramTriggerLegalityContext gate) =>
        IsIssuedTanbanInitialCandidate(gate.Candidate, gate.Context);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29040)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29040)]
    private bool PermitIssuedTanbanInitial(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "le:tanban" || frame.TriggerId != "mark-initial-entities" ||
            frame.ActivationId != frame.TriggerId ||
            frame.WindowContext is not { } context || frame.InstructionIndex is < 0 or > 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash ||
            !ExactOriginalHandLifecycleParent(frame, SkillProgramTriggerWindow.GameStarting) ||
            !OriginalHandBindingStarted(frame, SkillProgramTriggerWindow.GameStarting)) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId,
            frame.SkillInstanceId, frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        if (IsIssuedTanbanInitialCandidate(candidate, context)) return true;
        return frame.InstructionIndex == 1 &&
            ProgramEventHistory<TanbanInitializedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is [var initialized] &&
            initialized.Source == TanbanSource(frame) && initialized.GameplayHash == frame.GameplayHash;
    }

    [ProgramCapability]
    private sealed class LeDiaoChanTanbanCapability(GameEngine engine) : ILeDiaoChanTanbanHost
    {
        public SkillProgramStepOutcome InitializeTanban(ProgramSkillFrame frame) => engine.InitializeTanban(frame);
        public SkillProgramStepOutcome SwapTanbanHandMarks(ProgramSkillFrame frame) => engine.SwapTanbanHandMarks(frame);
    }

    private SkillProgramStepOutcome InitializeTanban(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || !PermitIssuedTanbanInitial(frame) ||
            ProgramEventHistory<TanbanInitializedEvent>().Any(e => e.Source.OwnerSeat == frame.OwnerSeat) ||
            ProgramEventHistory<TanbanEntityMarkChangedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Tanban requires its exact frozen opening mother and initializes once per owner.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(f => f.Id == frame.WindowContext!.ParentFrameId);
        var entities = parent.TanbanInitialEntities!.Where(e => e.OwnerSeat == frame.OwnerSeat).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e =>
            !_cardMovements.Any(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId &&
                m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OwnerSeat))))
            throw new InvalidOperationException("Tanban cannot mark an entity without its exact frozen opening-Hand provenance.");
        var source = TanbanSource(frame);
        foreach (var entity in entities)
            AdvanceEventRulesAndQueueFact(new TanbanEntityMarkChangedEvent(frame.Id, source, frame.GameplayHash,
                entity.OwnerSeat, entity.CardId, entity.PrintedKind, entity.EntrySequence,
                HasTanbanEntityMark(entity.CardId), true));
        AdvanceEventRulesAndQueueFact(new TanbanInitializedEvent(frame.Id, source, frame.GameplayHash, parent.Id, entities.Length));
        return SkillProgramStepOutcome.Continue;
    }
}
