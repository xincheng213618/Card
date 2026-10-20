namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsResponseCompletionContinuation(ProgramCardContinuation continuation) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletion.IsResponseCompletionContinuation(continuation);

    // A native cost child can grant the first observer. Collect live bindings
    // after those children return, rather than using a premature live-owner gate.
    private bool HasCardResponseCompletedObserver(CardActionContext action, ProgramCardContinuation continuation) =>
        GetRuntimeModule<ResponseCompletionRuntime>().HasCardResponseCompletedObserver(action, continuation);

    // The Duel's then-clause must wait for its actual response-payment children,
    // including when the catalogue has no response-completed skill observer.
    private bool IsJueshengPaidDuelResponse(CardActionContext action, ProgramCardContinuation continuation) =>
        GetRuntimeModule<ResponseCompletionRuntime>().IsJueshengPaidDuelResponse(action, continuation);

    private bool TryBeginCardResponseCompleted(CardAttackHandle? attack, CardActionContext action,
        ProgramCardContinuation originalContinuation) =>
        GetRuntimeModule<ResponseCompletionRuntime>().TryBeginCardResponseCompleted(attack?.ResolutionId, action, originalContinuation);

    private CardMovementRecord[] CaptureResponseCompletionCosts(CardActionContext a, ProgramCardContinuation continuation, long parentFrameId) =>
        GetRuntimeModule<ResponseCompletionRuntime>().CaptureResponseCompletionCosts(a, continuation, parentFrameId);

    // Extended Nullification uses the ordinary multi-card Respond/ResponseFinished
    // route; the single native counterspell uses Nullification/NullificationFinished.
    private static bool IsResponseCompletionEntryReason(CardActionContext action, ProgramCardContinuation continuation, CardMoveReason reason) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletion.IsResponseCompletionEntryReason(action,continuation,reason);
    private static CardMoveReason ResponseCompletionFinishReason(CardMovementRecord entered, ProgramCardContinuation continuation) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletion.ResponseCompletionFinishReason(entered,continuation);

    private bool IsResponseCompletionEntry(CardActionContext action, ProgramCardContinuation continuation,
        long parentFrameId, CardActionCost cost, CardMovementRecord moved) =>
        GetRuntimeModule<ResponseCompletionRuntime>().IsResponseCompletionEntry(action, continuation, parentFrameId, cost, moved);

    private static CardResponseCompletedEvent ResponseCompletionFact(ProgramCardTriggerWindowFrame window) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletion.ResponseCompletionFact(window);

    // This is an observer projection, never a second accepted Use/Response. A
    // faction provider actually plays the supplied card; the original principal
    // still owns the native response and every old completed-use continuation.
    private static CardActionContext ResponseCompletionActorAction(ProgramCardTriggerWindowFrame window) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ResponseCompletion.ResponseCompletionActorAction(window);

    private bool IsResponseCompletionCostFinished(ProgramCardTriggerWindowFrame window, CardActionCost cost) =>
        GetRuntimeModule<ResponseCompletionRuntime>().IsResponseCompletionCostFinished(window, cost);

    private bool IsResponseCompletionProgramDuelParent(ProgramSkillFrame frame, ProgramCardContinuation continuation) =>
        GetRuntimeModule<ResponseCompletionRuntime>().IsResponseCompletionProgramDuelParent(frame, continuation);

    private bool ValidResponseCompletionParent(ProgramCardTriggerWindowFrame window) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ValidResponseCompletionParent(window);

    private bool ValidResponseCompletionWindow(ProgramCardTriggerWindowFrame window) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ValidResponseCompletionWindow(window);

    private bool TryDrainCardResponseCompletionCosts(ProgramCardTriggerWindowFrame supplied) =>
        GetRuntimeModule<ResponseCompletionRuntime>().TryDrainCardResponseCompletionCosts(supplied);

    private bool ResponseCompletionFirstChild(ProgramCardTriggerWindowFrame window, ResolutionFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ResponseCompletionFirstChild(window, child);

    private bool ReturnCardResponseCompletionMovement(CardsMovedTriggerWindowFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ReturnCardResponseCompletionMovement(child);

    private void ContinueCardResponseAfterCompletion(CardAttackHandle? attack, ProgramCardTriggerWindowFrame window) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ContinueCardResponseAfterCompletion(attack?.ResolutionId, window);

    private bool ResponseCompletionStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        GetRuntimeModule<ResponseCompletionRuntime>().ResponseCompletionStructuralEdge(parent, child);
}
