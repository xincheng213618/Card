namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    // Gift evaluation reads these public-evidence beliefs and policy only. The
    // worker gets fresh mutable storage; no live brain or match RNG is shared.
    internal SimpleAiBrain ForkParticipantDecision()
    {
        var copy = new SimpleAiBrain(Seat, 1, _policyVersion, _includeJudgmentMaterials);
        foreach (var item in _rebelSuspicion) copy._rebelSuspicion.Add(item.Key, item.Value);
        foreach (var item in _nationalEnemySuspicion) copy._nationalEnemySuspicion.Add(item.Key, item.Value);
        return copy;
    }

    internal ChoiceId ChooseOptionalOwnedCardGift(GameSnapshot view, int recipientSeat,
        IReadOnlyList<PromptChoice> choices, ChoiceId decline)
    {
        var self = view.Players.Single(player => player.Seat == Seat);
        var recipient = view.Players.Single(player => player.Seat == recipientSeat);
        if (GetTacticalSupport(view, self.Role ?? Role.Renegade, recipient) <= 0) return decline;
        var owned = self.Hand.Concat(self.Equipment).ToDictionary(card => card.Id);
        return choices.Where(choice => choice.Cards.Count == 1 && owned.ContainsKey(choice.Cards[0]))
            .OrderBy(choice => CardCatalog.Get(owned[choice.Cards[0]].Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards[0]).FirstOrDefault()?.Id ?? decline;
    }
}
