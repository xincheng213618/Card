using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ActorHandLimitTargetPrograms
{
    internal static CardActionContext ActorHandLimitTargetCollectionAction(CardUseFrame use)
    {
        var action = use.Action!;
        if (action.TargetSeats.SequenceEqual(use.TargetSeats)) return action;
        // A paid Slash tail can redirect after its one original finalize. This
        // projection belongs only to the new collector, never to an old window.
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat,
            action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            action.EffectiveKind, use.TargetSeats, action.PhysicalCards, action.ConversionChain,
            action.EffectiveKind == CardKind.BorrowedSword
                ? use.TargetSeats.Where((_, index) => index % 2 == 0).ToArray() : use.TargetSeats,
            action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin);
    }
}
