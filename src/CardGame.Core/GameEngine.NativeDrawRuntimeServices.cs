namespace CardGame.Core;

public sealed partial class GameEngine
{
    // These adapters expose draw facts and existing native operations only. The
    // independent runtimes own every cursor decision and keep receipts on frames.
    [ProgramCapability]
    private sealed class NativeDrawProducerServices(GameEngine engine) : INativeDrawProducerQueries, INativeDrawProducerOperations
    {
        public int ResolveParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference) => engine.ResolveProgramParticipant(frame, reference);
        public int BoundCardCount(ProgramSkillFrame frame, string bind) => engine.GetProgramCardSet(frame, bind).CardIds.Count;
        public int PhaseSkillUsage(ProgramSkillFrame frame, string usageId) => engine.GetProgramPhaseSkillUsage(frame, usageId);
        public bool HasAttemptCapability => engine.HasNativeDrawAttemptCapability;
        public bool IsGameStartingDraw(ProgramSkillFrame frame) => engine.IsExactGameStartingDrawProgram(frame);
        public bool IsGameStartingGainDraw(ProgramSkillFrame frame) => engine.IsExactGameStartingGainDrawProgram(frame);
        public bool IsLiushiClearDraw(ProgramSkillFrame frame) => engine.IsLiushiClearDraw(frame);
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool IsAlive(int seat) => engine._players[seat].IsAlive;
        public bool HasSkillInstance(int seat, string skillId, string skillInstanceId) =>
            engine.HasRuntimeSkillInstance(engine._players[seat], skillId, skillInstanceId);
        public bool CanApplyPrepDiscardImmediateDraw(ProgramSkillFrame frame) => engine.CanApplyPrepDiscardImmediateDraw(frame);
        public Winner Winner => engine._winner;
        public long PairBenefitSequence => engine.PairBenefitSequence;
        public long MovementSequence => engine.OutsidePhaseMovementSequence;
        public ProgramExecutionPlan ResolvePlan(ProgramSkillFrame frame) =>
            ProgramInstructionResolver.Default.Resolve(frame, engine._contentRegistry.GetSkill(frame.SkillId).Program!);
        public bool HasOperationReturn(SkillProgramEffectOp operation) => ProgramNativeDrawReturns.Value.ContainsKey(operation);
        public IEnumerable<NativeDrawAttemptDecidedEvent> DecidedEvents() => engine.ProgramEventHistory<NativeDrawAttemptDecidedEvent>();
        public int? ResolveDrawCount(ProgramSkillFrame frame, int targetSeat, int amount, SkillProgramNumberExpression? expression) =>
            engine.ResolveProgramDrawCount(frame, targetSeat, amount, expression);
        public void DrawSelectedTargets(long id, int count, CardMoveReason reason) => engine.DrawProgramSelectedTargets(id, count, reason);
        public void DrawProgramCards(long id, int seat, int count, string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason) =>
            engine.DrawProgramCards(id, seat, count, null, resultBind, visibility, reason);
        public IReadOnlyList<int> DrawCards(int seat, int count, bool log, CardMoveReason reason) => engine.DrawCards(engine._players[seat], count, log, reason);
        public void ReturnOperation(SkillProgramEffectOp operation, ProgramSkillFrame frame, NativeDrawOutcome outcome) =>
            ProgramNativeDrawReturns.Value[operation](engine, frame, outcome);
        public void CaptureLiushiPayment(ProgramSkillFrame frame, int requested, long before, long after, int actual, long? attemptId, bool replaced) =>
            engine.CaptureLiushiClearDrawPayment(frame, requested, before, after, actual, attemptId, replaced);
        public void CompleteDrawCards(long id, int seat, string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason,
            long fixedRecipientSequenceBefore, IReadOnlyList<int> drawn) =>
            engine.CompleteProgramDrawCards(id, seat, resultBind, visibility, reason, fixedRecipientSequenceBefore, drawn);
        public void CancelBinding(ProgramSkillFrame frame, string reason) => engine.CancelProgramBindingAndCleanup(frame, reason);
        public bool BeginRecoveryReplacement(long id) => engine.TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program);
        public bool BeginCharacterStateWindow(long id) => engine.TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program);
        public bool BeginHpChangedWindow(long id) => engine.TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program);
        public bool BeginCardsMovedWindow(long id) => engine.TryBeginCardsMovedProgramWindow(id);
        public bool BeginSkillsChangedWindow(long id) => engine.TryBeginAdvancedSkillsChanged(id);
    }

    [ProgramCapability]
    private sealed class NativeDrawAttemptServices(GameEngine engine) : INativeDrawAttemptQueries, INativeDrawAttemptOperations
    {
        public bool HasAttemptCapability => engine.HasNativeDrawAttemptCapability;
        public bool SetupComplete => engine._setupComplete;
        public bool IsAlive(int seat) => engine._players[seat].IsAlive;
        public bool IsValidSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public Winner Winner => engine._winner;
        public int TurnNumber => engine._turnNumber;
        public int TurnOwnerSeat => engine._turnProgression.OwnerSeat;
        public ActualDiscardRecoveryPhaseKey? ActualPhase => engine.CurrentActualDiscardRecoveryPhase();
        public long MovementSequence => engine.OutsidePhaseMovementSequence;
        public SkillProgramTriggerFacts CaptureTriggerFacts(int seat) => engine.CaptureProgramTriggerFacts(engine._players[seat]);
        public IReadOnlyList<ProgramTriggerCandidate> CollectCandidates(int seat, SkillProgramTriggerFacts facts) =>
            engine.CollectEligibleProgramTriggerCandidates(engine._players[seat], SkillProgramTriggerWindow.NativeDrawStarting, facts);
        public IEnumerable<CardMovementRecord> Movements()
        {
            foreach (var movement in engine._cardMovements) yield return movement;
        }
        public IEnumerable<NativeDrawAttemptStartedEvent> StartedEvents() => engine.ProgramEventHistory<NativeDrawAttemptStartedEvent>();
        public IEnumerable<NativeDrawAttemptDecidedEvent> DecidedEvents() => engine.ProgramEventHistory<NativeDrawAttemptDecidedEvent>();
        public void CompleteLingkongReplacement(ProgramSkillFrame frame, NativeDrawAttemptFrame attempt, NativeDrawReplacementResult replacement) =>
            engine.CompleteLingkongNativeDrawReplacement(frame, attempt, replacement);
    }
}
