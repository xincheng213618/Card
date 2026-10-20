namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CardSupplyCompletion
{
    internal static CardActionContext CardSupplyCompletionActorAction(ProgramCardTriggerWindowFrame window)
    {
        var a = window.Action; var r = window.CardSupplyCompletion!;
        // This trusted observer projection remains a Use. It is not a new accepted
        // response and does not change who owns the underlying Slash's Use.
        return new(a.ActionId, a.ParentActionId, a.Type, r.ProviderSeat, r.ProviderSeat, a.RequesterSeat, a.ResponderSeat,
            a.OpponentSeat, a.EffectiveKind, a.TargetSeats, a.PhysicalCards, a.ConversionChain, a.DesignatedTargetSeats,
            a.EffectiveSuit, a.EffectiveRank, a.EffectiveIsRed, a.FactionOrigin);
    }

    internal static CardSupplyCompletedEvent CardSupplyCompletionFact(ProgramCardTriggerWindowFrame window)
    {
        var r = window.CardSupplyCompletion!;
        return new(window.Id, r.ActionId, r.ParentFrameId, r.RequesterSeat, r.ProviderSeat, r.EffectiveKind, r.ActualTurnNumber, r.ActualTurnOwnerSeat);
    }
}
