namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsHumanControlledSeat(int seat) => _options.HumanSeats is { } seats
        ? seats.Contains(seat) : seat == _options.HumanSeat;

    private int HumanDecisionSeat => _pendingDecision is { } pending && IsHumanControlledSeat(pending.PlayerSeat)
        ? pending.PlayerSeat : throw new InvalidOperationException("There is no human decision awaiting input.");

    /// <summary>Only the acting seat receives its legal actions; this query never advances the match.</summary>
    public IReadOnlyList<LegalAction> GetLegalActions(int actorSeat)
    {
        ValidatePlayerSeat(actorSeat, nameof(actorSeat));
        if (GetPendingDecisionForActor(actorSeat) is not { } pending) return [];
        return pending.Kind == DecisionKind.PlayCard ? BuildLegalActions(_players[actorSeat])
            : pending.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash
                ? BuildNationalRevealActions(_players[actorSeat]) : [];
    }
}
