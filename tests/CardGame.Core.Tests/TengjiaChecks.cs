using CardGame.Core;

internal static class TengjiaChecks
{
    public static void OrdinarySlashImmunityAndLegacyBoundary()
    {
        var boundary = TengjiaScenario.FindOrdinarySlash();
        var hp = boundary.Game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp;
        var result = boundary.Game.Submit(new PlayCardCommand(
            boundary.SourceSeat, boundary.SlashAction.CardId!.Value, boundary.SlashAction.TargetSeats,
            boundary.Game.Revision, boundary.Game.PendingDecision!.PromptId, boundary.SlashAction.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "Tengjia Slash was rejected.");
        var armor = boundary.Game.Events.Select(item => item.Payload).OfType<ArmorEffectAppliedEvent>().LastOrDefault(item =>
            item.ArmorCard == CardKind.Tengjia && item.TargetSeat == boundary.TargetSeat);
        Require(armor is { IncomingCard: CardKind.Slash } &&
                boundary.Game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp == hp &&
                boundary.Game.Events.Select(item => item.Payload).OfType<ResponseRequestedEvent>().All(item =>
                    item.TargetSeat != boundary.TargetSeat || item.IncomingCard != CardKind.Slash),
            "Tengjia must make an ordinary Slash ineffective before opening a Dodge response.");

        var legacy = GameReplay.Restore(RoundTrip(boundary.BeforeSlash) with { RulesVersion = 51 }, boundary.Registry);
        var action = legacy.GetHumanLegalActions().Single(candidate =>
            candidate.CardId == boundary.SlashAction.CardId && candidate.TargetSeat == boundary.TargetSeat);
        result = legacy.Submit(new PlayCardCommand(boundary.SourceSeat, action.CardId!.Value, action.TargetSeats,
            legacy.Revision, legacy.PendingDecision!.PromptId, action.PlayedCardKind));
        Require(result.Accepted && legacy.Events.Select(item => item.Payload).All(item =>
                item is not ArmorEffectAppliedEvent { ArmorCard: CardKind.Tengjia }),
            "Rules v51 must preserve the pre-Tengjia armor behavior.");

        VerifyFireDamageIncrease();
    }

    private static void VerifyFireDamageIncrease()
    {
        var boundary = TengjiaScenario.FindFireSlash();
        var hp = boundary.Game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp;
        var result = boundary.Game.Submit(new PlayCardCommand(
            boundary.SourceSeat, boundary.SlashAction.CardId!.Value, boundary.SlashAction.TargetSeats,
            boundary.Game.Revision, boundary.Game.PendingDecision!.PromptId, boundary.SlashAction.PlayedCardKind));
        Require(result.Accepted, result.Error?.Message ?? "Tengjia Fire Slash was rejected.");
        if (boundary.Game.PendingDecision is { Kind: DecisionKind.RespondDodge, PlayerSeat: var seat } prompt &&
            seat == boundary.TargetSeat)
        {
            result = boundary.Game.Submit(new AnswerPromptCommand(seat, prompt.PromptId,
                prompt.Choices.First(choice => choice.Cards.Count == 0).Id, boundary.Game.Revision));
            Require(result.Accepted, result.Error?.Message ?? "Tengjia target could not decline Dodge.");
        }

        var increased = boundary.Game.Events.Select(item => item.Payload)
            .OfType<TengjiaFireDamageIncreasedEvent>().Last(item => item.TargetSeat == boundary.TargetSeat);
        var damage = boundary.Game.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>().Last(item => item.TargetSeat == boundary.TargetSeat);
        Require(increased.BaseAmount == 1 && increased.ModifiedAmount == 2 && damage.Amount == 2 &&
                boundary.Game.CreateSnapshot(boundary.SourceSeat, revealAll: true).Players[boundary.TargetSeat].Hp == hp - 2,
            "Tengjia must publicly increase one Fire Slash damage from one to two.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
