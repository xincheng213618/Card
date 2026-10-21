namespace CardGame.Core;

// Character runtimes issue an operation draw without depending on an engine
// bridge. The existing producer still owns payment, its frame and typed return.
internal interface IProgramOperationDraws : IProgramRuntimeOperations
{
    SkillProgramStepOutcome BeginOperationProgramDraw(ProgramSkillFrame frame,
        int recipientSeat, int requestedCount, CardMoveReason reason);
}

internal interface INativeDrawProducerQueries : IProgramSelectionQuery
{
    int ResolveParticipant(ProgramSkillFrame frame, ProgramParticipantReference reference);
    int BoundCardCount(ProgramSkillFrame frame, string bind);
    int PhaseSkillUsage(ProgramSkillFrame frame, string usageId);
    bool HasAttemptCapability { get; }
    bool IsGameStartingDraw(ProgramSkillFrame frame);
    bool IsGameStartingGainDraw(ProgramSkillFrame frame);
    bool IsLiushiClearDraw(ProgramSkillFrame frame);
    bool IsValidSeat(int seat);
    bool IsAlive(int seat);
    bool HasSkillInstance(int seat, string skillId, string skillInstanceId);
    bool CanApplyPrepDiscardImmediateDraw(ProgramSkillFrame frame);
    Winner Winner { get; }
    long PairBenefitSequence { get; }
    long MovementSequence { get; }
    ProgramExecutionPlan ResolvePlan(ProgramSkillFrame frame);
    bool HasOperationReturn(SkillProgramEffectOp operation);
    IEnumerable<NativeDrawAttemptDecidedEvent> DecidedEvents();
}

internal interface INativeDrawProducerOperations : IProgramRuntimeOperations
{
    int? ResolveDrawCount(ProgramSkillFrame frame, int targetSeat, int amount, SkillProgramNumberExpression? expression);
    void DrawSelectedTargets(long id, int count, CardMoveReason reason);
    void DrawProgramCards(long id, int seat, int count, string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason);
    IReadOnlyList<int> DrawCards(int seat, int count, bool log, CardMoveReason reason);
    void ReturnOperation(SkillProgramEffectOp operation, ProgramSkillFrame frame, NativeDrawOutcome outcome);
    void CaptureLiushiPayment(ProgramSkillFrame frame, int requested, long before, long after, int actual, long? attemptId, bool replaced);
    void CompleteDrawCards(long id, int seat, string? resultBind, SkillProgramCardSetVisibility visibility, CardMoveReason reason,
        long fixedRecipientSequenceBefore, IReadOnlyList<int> drawn);
    void CancelBinding(ProgramSkillFrame frame, string reason);
    bool BeginRecoveryReplacement(long id);
    bool BeginCharacterStateWindow(long id);
    bool BeginHpChangedWindow(long id);
    bool BeginCardsMovedWindow(long id);
    bool BeginSkillsChangedWindow(long id);
}

internal interface INativeDrawAttemptQueries : IProgramSelectionQuery
{
    bool HasAttemptCapability { get; }
    bool SetupComplete { get; }
    bool IsAlive(int seat);
    bool IsValidSeat(int seat);
    Winner Winner { get; }
    int TurnNumber { get; }
    int TurnOwnerSeat { get; }
    ActualDiscardRecoveryPhaseKey? ActualPhase { get; }
    long MovementSequence { get; }
    SkillProgramTriggerFacts CaptureTriggerFacts(int seat);
    IReadOnlyList<ProgramTriggerCandidate> CollectCandidates(int seat, SkillProgramTriggerFacts facts);
    IEnumerable<CardMovementRecord> Movements();
    IEnumerable<NativeDrawAttemptStartedEvent> StartedEvents();
    IEnumerable<NativeDrawAttemptDecidedEvent> DecidedEvents();
    NativeDrawPileSlashProof? DrawPileSlashReplacementProof(ProgramSkillFrame frame, NativeDrawAttemptContext context);
    bool IsIssuedDrawPileSlashReplacement(NativeDrawAttemptContext context, NativeDrawReplacementResult replacement);
}

internal interface INativeDrawAttemptOperations : IProgramRuntimeOperations
{
    void CompleteLingkongReplacement(ProgramSkillFrame frame, NativeDrawAttemptFrame attempt, NativeDrawReplacementResult replacement);
}

internal interface INativeDrawAttemptRuntime : IProgramRuntimeOperations
{
    bool TryBegin(ResolutionFrame parent);
}
