namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsResponseCompletionSilverLionAttempt(IReadOnlyList<CardMovementRecord> costs, RecoveryAttempt a) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.IsResponseCompletionSilverLionAttempt(costs,a);
    private static bool IsResponseCompletionSilverLionHealth(IReadOnlyList<CardMovementRecord> costs, HpChangeContext h) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.IsResponseCompletionSilverLionHealth(costs,h);
    private static bool SameResponseCompletionRecovery(RecoveryAttempt a, RecoveryAttempt b) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletionHealth.SameResponseCompletionRecovery(a,b);

    private bool TryDrainResponseCompletionHealth(ProgramCardTriggerWindowFrame supplied) =>
        GetRuntimeModule<ResponseCompletionRuntime>().TryDrainResponseCompletionHealth(supplied);

    private bool IsResponseCompletionHealthChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().IsResponseCompletionHealthChild(window, child);
    private bool ReturnCardResponseCompletionRecovery(RecoveryReplacementFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ReturnCardResponseCompletionRecovery(child);
    private bool ReturnCardResponseCompletionHpChange(HpChangedTriggerWindowFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ReturnCardResponseCompletionHpChange(child);
}
