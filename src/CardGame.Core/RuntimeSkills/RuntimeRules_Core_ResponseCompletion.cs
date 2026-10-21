namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ResponseCompletion
{
    internal static bool IsResponseCompletionContinuation(ProgramCardContinuation continuation) => continuation is
        ProgramCardContinuation.Dodge or ProgramCardContinuation.DuelSlash or ProgramCardContinuation.GroupResponse or
        ProgramCardContinuation.FactionDefenseDodge or ProgramCardContinuation.FactionSlashDuelSlash or
        ProgramCardContinuation.FactionSlashGroupResponse or ProgramCardContinuation.NullificationResponse or ProgramCardContinuation.QizhengResponse;

    // Extended Nullification uses the ordinary multi-card Respond/ResponseFinished
    // route; the single native counterspell uses Nullification/NullificationFinished.
    internal static bool IsResponseCompletionEntryReason(CardActionContext action, ProgramCardContinuation continuation, CardMoveReason reason) =>
        continuation == ProgramCardContinuation.NullificationResponse
            ? reason == CardMoveReasons.Nullification || action.ConversionChain.Count > 0 && reason == CardMoveReasons.Respond
            : reason == CardMoveReasons.Respond;

    internal static CardMoveReason ResponseCompletionFinishReason(CardMovementRecord entered, ProgramCardContinuation continuation) =>
        entered.Reason == CardMoveReasons.Nullification || entered.Reason.Value == "conversion.declaration.pay" &&
        continuation == ProgramCardContinuation.NullificationResponse ? CardMoveReasons.NullificationFinished : CardMoveReasons.ResponseFinished;

    internal static CardResponseCompletedEvent ResponseCompletionFact(ProgramCardTriggerWindowFrame window)
    {
        var r = window.ResponseCompletion!; var a = window.Action;
        return new(a.ActionId, r.ParentFrameId, r.AttackOwnerFrameId, r.CompletionActorSeat, a.ProviderSeat,
            a.EffectiveKind, r.OriginalContinuation, r.ActualTurnNumber, r.ActualTurnOwnerSeat, a.ActorSeat);
    }

    // This is an observer projection, never a second accepted Use/Response. A
    // faction provider actually plays the supplied card; the original principal
    // still owns the native response and every old completed-use continuation.
    internal static CardActionContext ResponseCompletionActorAction(ProgramCardTriggerWindowFrame window)
    {
        var a = window.Action; var r = window.ResponseCompletion!;
        if (r.CompletionActorSeat == a.ActorSeat) return a;
        return new(a.ActionId, a.ParentActionId, a.Type, r.CompletionActorSeat, a.ProviderSeat, a.RequesterSeat,
            r.CompletionActorSeat, a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain,
            a.DesignatedTargetSeats, a.EffectiveSuit, a.EffectiveRank, a.EffectiveIsRed, a.FactionOrigin);
    }
}
