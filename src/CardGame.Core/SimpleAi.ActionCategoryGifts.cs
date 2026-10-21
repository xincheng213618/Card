namespace CardGame.Core;

public sealed partial class SimpleAiBrain
{
    public (ChoiceId Choice, AiThoughtRecord Thought) ChooseActionCategoryGiftProvider(
        GameSnapshot view, CardKind usedKind, IReadOnlyList<PromptChoice> choices, int thoughtSequence)
    {
        if (choices.Count == 0 || choices.Any(choice => choice.Targets.Count != 1))
            throw new InvalidOperationException("A category gift provider requires published single-target choices.");
        var role = view.Players.Single(player => player.Seat == Seat).Role ?? Role.Renegade;
        var excluded = CardCatalog.Get(usedKind).CategoryName;
        var scored = choices.Select(choice =>
        {
            var target = view.Players.Single(player => player.Seat == choice.Targets[0]);
            var publicGift = target.Equipment.Any(card => CardCatalog.Get(card.Kind).CategoryName != excluded);
            var canProvide = target.HandCount > 0 || publicGift;
            var score = canProvide ? GetTacticalSupport(view, role, target) * 100d +
                Math.Min(target.HandCount, 8) + (publicGift ? 4d : 0d) : -1000d;
            return (Choice: choice, Candidate: new AiCandidateScore(
                new(LegalActionKind.UseProgramSkill, null, target.Seat, choice.Description, TargetSeats: choice.Targets),
                Math.Round(score, 3), "优先向判断为队友且有牌可交的角色索牌；只读取公开关系、手牌数和装备。"));
        }).ToArray();
        var selected = scored.OrderByDescending(item => item.Candidate.Score)
            .ThenBy(item => item.Choice.Targets[0]).First();
        return (selected.Choice.Id, new(thoughtSequence, view.TurnNumber, Seat, selected.Choice.Description,
            scored.Select(item => item.Candidate).OrderByDescending(candidate => candidate.Score).ToArray(),
            "根据公开行动推断队友，避免向无牌可交的角色索牌。"));
    }

    public ChoiceId ChooseActionCategoryGift(GameSnapshot view, int recipientSeat, IReadOnlyList<PromptChoice> choices)
    {
        var decline = choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "different-action-category-decline");
        return ChooseOptionalOwnedCardGift(view, recipientSeat, choices, decline.Id);
    }
}
