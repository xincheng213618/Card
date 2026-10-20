namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QiqinObtainReason = "skill-program.le-xiao-qiao.qiqin.obtain";
    private bool HasLeXiaoQiaoQiqinCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeQiqinInitialEntities);
    private static CardConversionSource QiqinSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_xiao_qiao_le_LeXiaoQiaoQiqin.QiqinSource(f);
    private bool HasQiqinEntityMark(int id) => HasLeXiaoQiaoQiqinCapability && ProgramEventHistory<QiqinInitialEntityMarkedEvent>().Any(e => e.CardId == id);
    private bool HasQiqinHandEntityMark(int owner, int id) => IsValidPlayerSeat(owner) &&
        _cardZones.GetLocation(id) == CardLocation.Hand(owner) && HasQiqinEntityMark(id);
    private bool IsQiqinHandLimitExempt(CharacterState owner, int id) => HasLeXiaoQiaoQiqinCapability &&
        HasRuntimeSkill(owner, "le:qiqin") && HasQiqinHandEntityMark(owner.Seat, id);

    private ProgramLifecycleTriggerWindowFrame CaptureQiqinInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeXiaoQiaoQiqinCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.QiqinInitialEntities is not null) throw new InvalidOperationException("Qiqin opening Hand is frozen only once.");
        var owners = window.Candidates.Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.InitializeQiqinInitialEntities }])
            .Select(c => c.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Hand(owner));
            if (entry is null || entry.CardKind != card.Kind) throw new InvalidOperationException("Qiqin initial entity needs its genuine opening entry.");
            return new QiqinInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { QiqinInitialEntities = entities };
    }
    private bool IsIssuedQiqinCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        HasLeXiaoQiaoQiqinCapability && c.SkillId == "le:qiqin" && c.BindingId == "mark-initial-entities" &&
        IsValidPlayerSeat(c.OwnerSeat) && _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash == c.GameplayHash &&
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.InitializeQiqinInitialEntities }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == c.OwnerSeat && _phase == TurnPhase.NotStarted &&
        _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.GameStarting, Continuation: ProgramLifecycleContinuation.CompleteGameStarting, QiqinInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<QiqinInitializedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28610,
        SkillProgramEffectOp.InitializeQiqinInitialEntities, SkillProgramEffectOp.ResolveQiqinDiscardPileQin)]
    private bool CanOfferQiqin(ProgramTriggerLegalityContext gate) => gate.Trigger.Id == "mark-initial-entities"
        ? IsIssuedQiqinCandidate(gate.Candidate, gate.Context)
        : gate.Trigger.Id == "collect-discard-qin" && gate.Context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
          gate.Context.OwnerSeat == gate.Candidate.OwnerSeat && gate.Context.SourceSeat == gate.Candidate.OwnerSeat &&
          _players[gate.Candidate.OwnerSeat].IsAlive;

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28610)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28610)]
    private bool PermitIssuedQiqin(ProgramSkillFrame f)
    {
        if (f.SkillId != "le:qiqin" || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } context ||
            f.InstructionIndex is < 0 or > 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        if (f.TriggerId == "collect-discard-qin") return ValidQiqinReceipt(f);
        if (f.TriggerId != "mark-initial-entities" || !ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.GameStarting)) return false;
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        return IsIssuedQiqinCandidate(c, context) || f.InstructionIndex == 1 &&
            ProgramEventHistory<QiqinInitializedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var issued] &&
            issued.Source == QiqinSource(f) && issued.GameplayHash == f.GameplayHash;
    }
    [ProgramCapability]
    private sealed class LeXiaoQiaoQiqinCapability(GameEngine engine) : ILeXiaoQiaoQiqinHost
    {
        public SkillProgramStepOutcome Initialize(ProgramSkillFrame f) => engine.InitializeQiqin(f);
        public SkillProgramStepOutcome Collect(ProgramSkillFrame f) => engine.BeginQiqinCollect(f);
    }
    private SkillProgramStepOutcome InitializeQiqin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || !PermitIssuedQiqin(f) || ProgramEventHistory<QiqinInitializedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("Qiqin requires its one exact frozen opening mother.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(s => s.Id == f.WindowContext!.ParentFrameId);
        var entities = parent.QiqinInitialEntities!.Where(e => e.OwnerSeat == f.OwnerSeat).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e =>
                _cardMovements.Count(m => m.Sequence == e.DealSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OwnerSeat)) != 1))
            throw new InvalidOperationException("Qiqin lost its actual initial physical entry invoice.");
        AdvanceEventRulesAndQueueFact(new QiqinInitializedEvent(f.Id, QiqinSource(f), f.GameplayHash, entities.Length));
        foreach (var e in entities) AdvanceEventRulesAndQueueFact(new QiqinInitialEntityMarkedEvent(f.Id, e.OwnerSeat, e.CardId, e.PrintedKind, e.DealSequence));
        return SkillProgramStepOutcome.Continue;
    }
    private SkillProgramStepOutcome BeginQiqinCollect(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Qiqin is not null || f.SkillId != "le:qiqin" || f.TriggerId != "collect-discard-qin" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Qiqin needs its real living owner's preparation and cannot obtain twice.");
        var entities = _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => HasQiqinEntityMark(c.Id))
            .Select(c => new QiqinCollectedEntity(c.Id, c.Kind)).ToArray();
        var r = new QiqinReceipt(QiqinSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, f.InstructionIndex,
            _turnNumber, _currentSeat, _movementSequence) { SequenceAfter = _movementSequence, Entities = entities };
        ReplaceRuntimeTop(f = f with { Qiqin = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new QiqinCollectStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.ActualTurn, r.TurnOwner, entities.Length, r.SequenceBefore));
        for (var i = 0; i < entities.Length; i++) AdvanceEventRulesAndQueueFact(new QiqinCollectEntityIssuedEvent(f.Id, i, entities[i].CardId, entities[i].PrintedKind));
        if (entities.Length != 0)
            MoveProgramCardsFromMultipleSources(entities.Select(e => e.CardId).ToArray(), CardLocation.Hand(f.OwnerSeat), new(QiqinObtainReason), (batchId, moves) =>
            {
                var active = GetActiveProgramFrame(f.Id); var current = active.Qiqin!;
                if (current.BatchId != 0 || moves.Count != entities.Length || moves.Where((m, i) =>
                        m.CardId != entities[i].CardId || m.CardKind != entities[i].PrintedKind || m.From != CardLocation.DiscardPile ||
                        m.To != CardLocation.Hand(f.OwnerSeat) || m.Reason.Value != QiqinObtainReason || m.Sequence != current.SequenceBefore + i + 1).Any())
                    throw new InvalidOperationException("Qiqin lost its one whole actual discard-pile obtain batch.");
                ReplaceRuntimeTop(active with { Qiqin = current with { BatchId = batchId, SequenceAfter = moves[^1].Sequence } });
                AdvanceEventRulesAndQueueFact(new QiqinCollectPaidEvent(f.Id, batchId, moves.Count, current.SequenceBefore, moves[^1].Sequence));
                for (var i = 0; i < moves.Count; i++) AdvanceEventRulesAndQueueFact(new QiqinCollectedEntityEvent(f.Id, i, moves[i].CardId, moves[i].CardKind, moves[i].Sequence));
            });
        ContinueQiqinCollect(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinueQiqinCollect(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Qiqin: not null } f || f.Id != id || _pendingDecision is not null) return;
        AssertQiqin(f);
        if (new ProgramSkillHost(this).TryStartPostInstructionWindow(id)) return;
        f = GetActiveProgramFrame(id);
        AdvanceEventRulesAndQueueFact(new QiqinCollectCompletedEvent(id, f.Qiqin!.WindowId, f.Qiqin.Entities.Count));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28610)]
    private bool ResumeQiqin(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Qiqin: not null } f || f.Id != id) return false; ContinueQiqinCollect(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28610)]
    private bool ReturnQiqinMovement(ProgramSkillFrame f)
    { if (f.Qiqin is null) return false; ContinueQiqinCollect(f.Id); return true; }
}
