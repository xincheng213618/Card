namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ShuangjiaMarkRuntime ShuangjiaMarks =>
        (ShuangjiaMarkRuntime)GetRuntimeModuleInstance(typeof(ShuangjiaMarkRuntime));
    private bool HasLeCaiWenJiShuangjiaCapability => ShuangjiaMarks.HasCapability;
    private static CardConversionSource ShuangjiaSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    // The physical mark outlives its original holder, zone and granting instance.
    private bool HasShuangjiaEntityMark(int cardId) => ShuangjiaMarks.HasEntityMark(cardId);
    private bool IsShuangjiaHandLimitExempt(CharacterState owner, int cardId) =>
        ShuangjiaMarks.IsHandLimitExempt(owner.Seat, cardId);

    private ProgramLifecycleTriggerWindowFrame CaptureShuangjiaInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasLeCaiWenJiShuangjiaCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.ShuangjiaInitialEntities is not null)
            throw new InvalidOperationException("Shuangjia's final opening Hand can only be frozen once.");
        var owners = window.Candidates.Where(candidate => GetProgramTrigger(candidate).Effects is
            [{ Op: SkillProgramEffectOp.InitializeShuangjiaInitialEntities }])
            .Select(candidate => candidate.OwnerSeat).Distinct().Order();
        var entities = owners.SelectMany(owner => GetHand(_players[owner]).Select(card =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == card.Id && m.To == CardLocation.Hand(owner));
            if (entry is null || entry.CardKind != card.Kind)
                throw new InvalidOperationException("A Shuangjia initial entity requires its actual opening-Hand entry.");
            return new ShuangjiaInitialEntity(owner, card.Id, card.Kind, entry.Sequence);
        })).ToArray();
        return window with { ShuangjiaInitialEntities = entities };
    }

    private bool IsIssuedShuangjiaCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        HasLeCaiWenJiShuangjiaCapability && candidate.SkillId == "le:shuangjia" &&
        candidate.BindingId == "mark-initial-entities" && IsValidPlayerSeat(candidate.OwnerSeat) &&
        _contentRegistry.GetSkill(candidate.SkillId).Program?.GameplayHash == candidate.GameplayHash &&
        GetProgramTrigger(candidate).Effects is [{ Op: SkillProgramEffectOp.InitializeShuangjiaInitialEntities }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == candidate.OwnerSeat &&
        _phase == TurnPhase.NotStarted &&
        _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.GameStarting, Continuation: ProgramLifecycleContinuation.CompleteGameStarting,
              ShuangjiaInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
        parent.Candidates[parent.CandidateIndex] == candidate &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<ShuangjiaInitializedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28410,
        SkillProgramEffectOp.InitializeShuangjiaInitialEntities)]
    private bool CanOfferShuangjia(ProgramTriggerLegalityContext gate) =>
        IsIssuedShuangjiaCandidate(gate.Candidate, gate.Context);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28410)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28410)]
    private bool PermitIssuedShuangjia(ProgramSkillFrame frame)
    {
        if (frame.SkillId != "le:shuangjia" || frame.TriggerId != "mark-initial-entities" ||
            frame.WindowContext is not { } context || frame.InstructionIndex is < 0 or > 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash != frame.GameplayHash ||
            !ExactOriginalHandLifecycleParent(frame, SkillProgramTriggerWindow.GameStarting)) return false;
        var candidate = new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId,
            frame.SkillInstanceId, frame.GameplayHash, GetProgramTrigger(frame).Priority, context.OccurrenceIndex);
        if (IsIssuedShuangjiaCandidate(candidate, context)) return true;
        // After the only instruction, preserve its completed right while the executor closes it.
        return frame.InstructionIndex == 1 &&
            ProgramEventHistory<ShuangjiaInitializedEvent>().Where(e => e.FrameId == frame.Id).ToArray() is [var initialized] &&
            initialized.Source == ShuangjiaSource(frame) && initialized.GameplayHash == frame.GameplayHash;
    }

    [ProgramCapability]
    private sealed class LeCaiWenJiShuangjiaCapability(GameEngine engine) : ILeCaiWenJiShuangjiaHost
    {
        public SkillProgramStepOutcome InitializeShuangjia(ProgramSkillFrame frame) => engine.InitializeShuangjia(frame);
    }

    private SkillProgramStepOutcome InitializeShuangjia(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.InstructionIndex != 1 || !PermitIssuedShuangjia(frame) ||
            ProgramEventHistory<ShuangjiaInitializedEvent>().Any(e => e.Source.OwnerSeat == frame.OwnerSeat))
            throw new InvalidOperationException("Shuangjia requires its exact frozen opening mother and initializes once.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(f => f.Id == frame.WindowContext!.ParentFrameId);
        var entities = parent.ShuangjiaInitialEntities!.Where(e => e.OwnerSeat == frame.OwnerSeat).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e =>
            !_cardMovements.Any(m => m.Sequence == e.DealSequence && m.CardId == e.CardId &&
                m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OwnerSeat))))
            throw new InvalidOperationException("Shuangjia cannot mark an entity without its exact opening-Hand provenance.");
        AdvanceEventRulesAndQueueFact(new ShuangjiaInitializedEvent(frame.Id, ShuangjiaSource(frame), frame.GameplayHash, entities.Length));
        foreach (var entity in entities)
            AdvanceEventRulesAndQueueFact(new ShuangjiaInitialEntityMarkedEvent(frame.Id, entity.OwnerSeat,
                entity.CardId, entity.PrintedKind, entity.DealSequence));
        return SkillProgramStepOutcome.Continue;
    }
}
