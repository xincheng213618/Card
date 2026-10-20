namespace CardGame.Core;

public sealed partial class GameEngine
{
    private HealthContinuationRuntime HealthContinuations => GetRuntimeModule<HealthContinuationRuntime>();

    // This family starts at an actually issued Slash payment batch. Native HP
    // and removal-recovery children have separate invoices and remain outside it.
    private TypedCommittedSlashPaymentCursorProjection ProjectTypedCommittedSlashPaymentCursor() =>
        HealthContinuations.ProjectTypedCommittedSlashPaymentCursor();

    private bool TypedCommittedSlashPaymentActionMatches(CardUseFrame use, CardActionContext action, int rootIndex) =>
        HealthContinuations.TypedCommittedSlashPaymentActionMatches(use, action, rootIndex);

    // A native Dodge's paid health invoices, including a faction supplier's,
    // belong to its response-completion window and retain the Slash owner ID.
    // Complex rescue/macro suffixes continue to use their own return protocol.
    private TypedResponseCompletionHealthCursorProjection ProjectTypedResponseCompletionHealthCursor() =>
        HealthContinuations.ProjectTypedResponseCompletionHealthCursor();

    private TypedPaidObserverSuffixProjection ProjectTypedPaidObserverSuffix(int firstIndex,
        Func<IReadOnlyList<IGameEvent>> history, bool exactOrdinaryHpProducer = false) =>
        HealthContinuations.ProjectTypedPaidObserverSuffix(firstIndex, history, exactOrdinaryHpProducer);

    private static bool TypedPaidDyingEntryReturnMatches(DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_TypedRecoveryDamageCursor.TypedPaidDyingEntryReturnMatches(dying,entry);

    private bool TypedPaidDyingEntryObserverMatches(ProgramLifecycleTriggerWindowFrame entry, ProgramSkillFrame observer,
        IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedPaidDyingEntryObserverMatches(entry, observer, history);

    private bool TypedOrdinaryPaidHpProducerMatches(ProgramSkillFrame producer, SkillProgramEffect effect,
        HpChangedTriggerWindowFrame hp) =>
        HealthContinuations.TypedOrdinaryPaidHpProducerMatches(producer, effect, hp);

    private static bool IsTypedRecoveryTargetSupported(RecoverProgramInstruction instruction) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_TypedRecoveryDamageCursor.IsTypedRecoveryTargetSupported(instruction);

    private bool UnsupportedTypedRecoveryTargetIsMalformed(ProgramSkillFrame producer, RecoverProgramInstruction instruction, int actualTarget) =>
        HealthContinuations.UnsupportedTypedRecoveryTargetIsMalformed(producer, instruction, actualTarget);

    private bool TypedResponseCompletionRecoveryMatches(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedResponseCompletionRecoveryMatches(recovery, history);

    // This pilot follows frame-owned returns, never skill/capability names.
    // Special payment producers retain their existing receipt validators.
    private TypedRecoveryDamageCursorProjection ProjectTypedRecoveryDamageCursor(long windowId) =>
        HealthContinuations.ProjectTypedRecoveryDamageCursor(windowId);

    private bool TypedRecoveryApplicationBoundsMatch(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryApplicationBoundsMatch(recovery, history);

    private static bool IsRecoveryCursorPrimitive(ProgramSkillInstruction? instruction) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_TypedRecoveryDamageCursor.IsRecoveryCursorPrimitive(instruction);

    private SkillProgramEffect? TypedRecoveryPausedEffect(ProgramSkillFrame producer) =>
        HealthContinuations.TypedRecoveryPausedEffect(producer);

    private bool DirectProgramRecoveryReturnMatches(ProgramSkillFrame producer, SkillProgramEffect effect,
        RecoverProgramInstruction instruction, RecoveryReplacementFrame recovery) =>
        HealthContinuations.DirectProgramRecoveryReturnMatches(producer, effect, instruction, recovery);

    private bool TypedRecoveryHpReturnMatches(RecoveryReplacementFrame recovery, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryHpReturnMatches(recovery, hp, history);

    private bool TypedRecoveryChosenCandidateMatches(RecoveryReplacementFrame recovery, IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryChosenCandidateMatches(recovery, history);

    private bool TypedRecoveryRewardReturnMatches(RecoveryReplacementFrame recovery, CardsMovedTriggerWindowFrame movement,
        IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryRewardReturnMatches(recovery, movement, history);

    private bool TypedRecoveryRewardReturnMatches(RecoveryReplacementFrame recovery, CardsMovedTriggerWindowFrame movement,
        IReadOnlyList<IGameEvent> history, ProgramSkillFrame? origin) =>
        HealthContinuations.TypedRecoveryRewardReturnMatches(recovery, movement, history, origin);

    private bool TypedRecoveryRewardBatchMatchesReceipt(RecoveryReplacementFrame recovery, CardMovementBatchContext batch) =>
        HealthContinuations.TypedRecoveryRewardBatchMatchesReceipt(recovery, batch);

    private bool TypedRecoveryProgramDyingMatches(ProgramSkillFrame producer, DyingFrame dying, IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryProgramDyingMatches(producer, dying, history);

    private bool TypedRecoveryDyingResponseMatches(DyingFrame dying, ProgramSkillFrame response, IReadOnlyList<IGameEvent> history) =>
        HealthContinuations.TypedRecoveryDyingResponseMatches(dying, response, history);

    private bool HasTypedRecoveryProgramDying() =>
        HealthContinuations.HasTypedRecoveryProgramDying();
}
