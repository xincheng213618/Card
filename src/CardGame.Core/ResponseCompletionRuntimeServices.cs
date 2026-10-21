namespace CardGame.Core;

// Individual facts keep the response's original journal and frame searches lazy.
// No mutable player, stack, or movement collection crosses this boundary.
internal interface IResponseCompletionFacts : IProgramSelectionQuery
{
    Winner Winner { get; }
    int TurnNumber { get; }
    int CurrentSeat { get; }
    int FrameCount { get; }
    ResolutionFrame FrameAt(int index);
    int PendingMovementBatchCount { get; }
    CardMovementBatchContext PendingMovementBatchAt(int index);
    int PendingHpChangeCount { get; }
    HpChangeContext PendingHpChangeAt(int index);
    bool ContainsMovement(CardMovementRecord movement);
    CardMovementRecord? LastMovement(Func<CardMovementRecord, bool> predicate);
    bool HasTriggerWindow(SkillProgramTriggerWindow window);
    bool UsesTriggerCondition(SkillProgramTriggerConditionKind condition);
    bool IsValidPlayerSeat(int seat);
    bool IsAlive(int playerIndex);
    int PlayerSeat(int playerIndex);
    SkillProgramTriggerFacts CaptureProgramTriggerFacts(int playerIndex);
    IReadOnlyList<ProgramTriggerCandidate> CollectProgramTriggerCandidates(int playerIndex, SkillProgramTriggerWindow window);
    SkillProgramTrigger GetProgramTrigger(ProgramTriggerCandidate candidate);
    SkillProgram? Program(string skillId);
    SkillProgramViewAs? ViewAsRule(CardConversionSource source);
    IEnumerable<IGameEvent> CompleteProgramEventHistory();
    NullificationWindowFrame? ActiveNullificationWindow { get; }
    long? ActiveDuelOwnerId { get; }
    int DuelOpponentSeat(long ownerId);
    int DuelResponderSeat(long ownerId);
    long? ActiveGroupOwnerId { get; }
    int GroupSourceSeat(long ownerId);
    CardKind? GroupRequiredCardKind(long ownerId);
    long? GroupCurrentAttackOwnerId(long ownerId);
}

// These proofs belong to existing native payment and card-use mechanisms. The
// response runtime owns the once-paid cursor and chooses when to ask each proof.
internal interface IResponseCompletionNativeProtocols : IProgramRuntimeOperations
{
    bool HasJiyunResponseCompletion(CardActionContext action, ProgramCardContinuation continuation);
    bool IsNativePublicPileOriginalActionCost(CardActionContext action, CardActionCost cost);
    bool IsJueshengDuelUse(long useId);
    bool HasGuanduChariotResponsePayment(CardActionContext action, ProgramCardContinuation continuation);
    bool HasChenglieResponseObserver(CardActionContext action, ProgramCardContinuation continuation);
    bool TryGetXieweiResponseCostMovement(CardActionContext action, int cardId, out CardMovementRecord movement);
    bool TryGetSiruiDestroyedResponseCost(CardActionContext action, int cardId, out CardMovementRecord movement);
    bool TryGetWushuangJinzhiResponseMovements(CardActionContext action, int cardId, out IReadOnlyList<CardMovementRecord> movements);
    CardMovementRecord? GuanduChariotResponsePayment(CardActionContext action, long parentFrameId,
        CardActionCost cost, ProgramCardContinuation continuation);
    bool IsProgramAlternativeCost(CardActionContext action, int cardId);
    bool IsExchangedCardClaim(long actionId, int cardId);
    bool IsWuZglReturnedCopyCost(CardActionContext action, CardActionCost cost, CardMovementRecord finish);
    bool HasNullificationEffectOwner(NullificationWindowFrame window);
    long? NullificationParentActionId(NullificationWindowFrame window);
    void CapturePairedColorResponseLink(ProgramCardTriggerWindowFrame window);
    void CaptureMaMiDiResponseLink(ProgramCardTriggerWindowFrame window);
    bool RemovePendingHpChange(HpChangeContext change);
    void RemovePendingMovementBatch(CardMovementBatchContext batch);
    CardsMovedTriggerWindowFrame? CreateCardsMovedProgramWindow(CardMovementBatchContext batch, long? resumeProgramFrameId);
    void CaptureLeCaoZhiWenlanQualification(CardActionContext action, SkillProgramTriggerWindow window);
    IReadOnlyList<ProgramCardTriggerCandidate> CollectSharedCardActionCandidates(CardActionContext action,
        SkillProgramTriggerWindow window, IReadOnlyList<int> eventTargets, bool? cardUseCausedDamage);
    bool TryBeginCompletedResponseUsePrograms(long? attackOwnerFrameId, CardActionContext action, ProgramCardContinuation continuation);
    NullificationWindowFrame ClearTieredRoundCounterspellResponse(NullificationWindowFrame window);
    void ContinueNullificationWindow(NullificationWindowFrame window);
    void ContinueFinishedCardResponse(long attackOwnerFrameId, CardActionContext action, ProgramCardContinuation continuation);
}

internal sealed partial class ResponseCompletionRuntime
{
    private readonly IResponseCompletionFacts _facts;
    private readonly IResponseCompletionNativeProtocols _native;
    private readonly IProgramRuntimeFrames _frames;
    private readonly IProgramRuntimeEvents _events;

    internal ResponseCompletionRuntime(IResponseCompletionFacts facts, IResponseCompletionNativeProtocols native,
        IProgramRuntimeFrames frames, IProgramRuntimeEvents events)
    {
        _facts = facts;
        _native = native;
        _frames = frames;
        _events = events;
    }

    private IEnumerable<ResolutionFrame> FrameFacts()
    {
        for (var index = 0; index < _facts.FrameCount; index++)
            yield return _facts.FrameAt(index);
    }

    private IEnumerable<CardMovementBatchContext> PendingMovementBatches()
    {
        for (var index = 0; index < _facts.PendingMovementBatchCount; index++)
            yield return _facts.PendingMovementBatchAt(index);
    }

    private IEnumerable<HpChangeContext> PendingHealthChanges()
    {
        for (var index = 0; index < _facts.PendingHpChangeCount; index++)
            yield return _facts.PendingHpChangeAt(index);
    }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class ResponseCompletionFacts(GameEngine engine) : IResponseCompletionFacts
    {
        public Winner Winner => engine._winner;
        public int TurnNumber => engine._turnNumber;
        public int CurrentSeat => engine._currentSeat;
        public int FrameCount => engine._resolutionStack.Count;
        public ResolutionFrame FrameAt(int index) => engine._resolutionStack[index];
        public int PendingMovementBatchCount => engine._pendingCardsMovedBatches.Count;
        public CardMovementBatchContext PendingMovementBatchAt(int index) => engine._pendingCardsMovedBatches[index];
        public int PendingHpChangeCount => engine._pendingHpChanges.Count;
        public HpChangeContext PendingHpChangeAt(int index) => engine._pendingHpChanges[index];
        public bool ContainsMovement(CardMovementRecord movement) => engine._cardMovements.Contains(movement);
        public CardMovementRecord? LastMovement(Func<CardMovementRecord, bool> predicate) => engine._cardMovements.LastOrDefault(predicate);
        public bool HasTriggerWindow(SkillProgramTriggerWindow window) => engine._contentRegistry.ProgramDependencies.HasTriggerWindow(window);
        public bool UsesTriggerCondition(SkillProgramTriggerConditionKind condition) => engine._contentRegistry.ProgramDependencies.UsesTriggerCondition(condition);
        public bool IsValidPlayerSeat(int seat) => engine.IsValidPlayerSeat(seat);
        public bool IsAlive(int playerIndex) => engine._players[playerIndex].IsAlive;
        public int PlayerSeat(int playerIndex) => engine._players[playerIndex].Seat;
        public SkillProgramTriggerFacts CaptureProgramTriggerFacts(int playerIndex) => engine.CaptureProgramTriggerFacts(engine._players[playerIndex]);
        public IReadOnlyList<ProgramTriggerCandidate> CollectProgramTriggerCandidates(int playerIndex, SkillProgramTriggerWindow window) =>
            engine.CollectProgramTriggerCandidates(engine._players[playerIndex], window);
        public SkillProgramTrigger GetProgramTrigger(ProgramTriggerCandidate candidate) => engine.GetProgramTrigger(candidate);
        public SkillProgram? Program(string skillId) => engine._contentRegistry.Skills.GetValueOrDefault(skillId)?.Program;
        public SkillProgramViewAs? ViewAsRule(CardConversionSource source) => engine.ViewAsRule(source);
        public IEnumerable<IGameEvent> CompleteProgramEventHistory() => engine.CompleteProgramEventHistory();
        public NullificationWindowFrame? ActiveNullificationWindow => engine.ActiveNullificationWindow;
        public long? ActiveDuelOwnerId => engine.ActiveDuel?.ResolutionId;
        public int DuelOpponentSeat(long ownerId) => new DuelHandle(engine, ownerId).OpponentSeat;
        public int DuelResponderSeat(long ownerId) => new DuelHandle(engine, ownerId).ResponderSeat;
        public long? ActiveGroupOwnerId => engine.ActiveGroupCard?.ResolutionId;
        public int GroupSourceSeat(long ownerId) => new GroupCardHandle(engine, ownerId).SourceSeat;
        public CardKind? GroupRequiredCardKind(long ownerId) => new GroupCardHandle(engine, ownerId).RequiredCardKind;
        public long? GroupCurrentAttackOwnerId(long ownerId) => new GroupCardHandle(engine, ownerId).CurrentAttack?.ResolutionId;
    }

    [ProgramCapability]
    private sealed class ResponseCompletionNativeProtocols(GameEngine engine) : IResponseCompletionNativeProtocols
    {
        public bool HasJiyunResponseCompletion(CardActionContext action, ProgramCardContinuation continuation) => engine.HasJiyunResponseCompletion(action, continuation);
        public bool IsNativePublicPileOriginalActionCost(CardActionContext action, CardActionCost cost) => engine.IsNativePublicPileOriginalActionCost(action, cost);
        public bool IsJueshengDuelUse(long useId) => engine.IsJueshengDuelUse(useId);
        public bool HasGuanduChariotResponsePayment(CardActionContext action, ProgramCardContinuation continuation) => engine.HasGuanduChariotResponsePayment(action, continuation);
        public bool HasChenglieResponseObserver(CardActionContext action, ProgramCardContinuation continuation) => engine.HasChenglieResponseObserver(action, continuation);
        public bool TryGetXieweiResponseCostMovement(CardActionContext action, int cardId, out CardMovementRecord movement) => engine.TryGetXieweiResponseCostMovement(action, cardId, out movement);
        public bool TryGetSiruiDestroyedResponseCost(CardActionContext action, int cardId, out CardMovementRecord movement) => engine.TryGetSiruiDestroyedResponseCost(action, cardId, out movement);
        public bool TryGetWushuangJinzhiResponseMovements(CardActionContext action, int cardId, out IReadOnlyList<CardMovementRecord> movements) => engine.TryGetWushuangJinzhiResponseMovements(action, cardId, out movements);
        public CardMovementRecord? GuanduChariotResponsePayment(CardActionContext action, long parentFrameId,
            CardActionCost cost, ProgramCardContinuation continuation) => engine.GuanduChariotResponsePayment(action, parentFrameId, cost, continuation);
        public bool IsProgramAlternativeCost(CardActionContext action, int cardId) => engine.IsProgramAlternativeCost(action, cardId);
        public bool IsExchangedCardClaim(long actionId, int cardId) => engine.IsExchangedCardClaim(actionId, cardId);
        public bool IsWuZglReturnedCopyCost(CardActionContext action, CardActionCost cost, CardMovementRecord finish) => engine.IsWuZglReturnedCopyCost(action, cost, finish);
        public bool HasNullificationEffectOwner(NullificationWindowFrame window) => engine.HasNullificationEffectOwner(window);
        public long? NullificationParentActionId(NullificationWindowFrame window) => engine.NullificationParentActionId(window);
        public void CapturePairedColorResponseLink(ProgramCardTriggerWindowFrame window) => engine.CapturePairedColorResponseLink(window);
        public void CaptureMaMiDiResponseLink(ProgramCardTriggerWindowFrame window) => engine.CaptureMaMiDiResponseLink(window);
        public bool RemovePendingHpChange(HpChangeContext change) => engine._pendingHpChanges.Remove(change);
        public void RemovePendingMovementBatch(CardMovementBatchContext batch) => engine._pendingCardsMovedBatches.Remove(batch);
        public CardsMovedTriggerWindowFrame? CreateCardsMovedProgramWindow(CardMovementBatchContext batch, long? resumeProgramFrameId) =>
            engine.CreateCardsMovedProgramWindow(batch, resumeProgramFrameId);
        public void CaptureLeCaoZhiWenlanQualification(CardActionContext action, SkillProgramTriggerWindow window) => engine.CaptureLeCaoZhiWenlanQualification(action, window);
        public IReadOnlyList<ProgramCardTriggerCandidate> CollectSharedCardActionCandidates(CardActionContext action,
            SkillProgramTriggerWindow window, IReadOnlyList<int> eventTargets, bool? cardUseCausedDamage) =>
            engine.CollectSharedCardActionCandidates(action, window, eventTargets, cardUseCausedDamage);
        public bool TryBeginCompletedResponseUsePrograms(long? attackOwnerFrameId, CardActionContext action, ProgramCardContinuation continuation) =>
            engine.TryBeginCompletedResponseUsePrograms(attackOwnerFrameId is { } id ? new CardAttackHandle(engine, id) : null, action, continuation);
        public NullificationWindowFrame ClearTieredRoundCounterspellResponse(NullificationWindowFrame window) => engine.ClearTieredRoundCounterspellResponse(window);
        public void ContinueNullificationWindow(NullificationWindowFrame window) => engine.ContinueNullificationWindow(window);
        public void ContinueFinishedCardResponse(long attackOwnerFrameId, CardActionContext action, ProgramCardContinuation continuation) =>
            engine.ContinueFinishedCardResponse(new CardAttackHandle(engine, attackOwnerFrameId), action, continuation);
    }
}
