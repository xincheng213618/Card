namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool BeginPindianClaims(PindianFrame frame)
    {
        var result = frame.Result ?? throw new InvalidOperationException("Pindian claim requires a revealed result.");
        var seats = frame.ClaimSeats ?? new[] { result.SourceSeat, result.OpponentSeat }
            .Where(seat => CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianClaim).Any() ||
                CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins).Any()).ToArray();
        while (frame.ClaimIndex < seats.Count)
        {
            var seat = seats[frame.ClaimIndex];
            var player = _players[seat];
            var claimAll = CardPolicies(player, SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins).FirstOrDefault();
            var binding = claimAll.Source is not null ? claimAll
                : CardPolicies(player, SkillProgramCardPolicyKind.PindianClaim).FirstOrDefault();
            var ids = claimAll.Source is not null
                ? seat == result.SourceSeat && !result.WonBy(result.OpponentSeat)
                    ? new[] { result.SourceCardId, result.OpponentCardId }
                        .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Order().ToArray()
                    : []
                : new[] { result.WonBy(seat)
                    ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId
                    : result.CardOf(seat) };
            if (!player.IsAlive || binding.Source is null || ids.Length == 0 ||
                ids.Any(id => _cardZones.GetLocation(id) != CardLocation.Processing))
            { frame = frame with { ClaimSeats = seats, ClaimIndex = frame.ClaimIndex + 1 }; continue; }
            frame = frame with { ClaimSeats = seats, PindianStep = PindianStep.ClaimResult };
            ReplaceRuntimeTop(frame);
            var skill = _contentRegistry.GetSkill(binding.Source.SkillId);
            var cards = ids.Select(id => _cardZones.CardsAt(CardLocation.Processing).Single(item => item.Id == id)).ToArray();
            var label = cards.Length == 1 ? $"获得【{cards[0].DisplayName}】"
                : $"获得{string.Join("和", cards.Select(card => $"【{card.DisplayName}】"))}";
            var choices = new[]
            {
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.claim-{seat}-{ids[0]}"), label, ids, [],
                    new Dictionary<string,string> { ["action"] = "pindian-claim", ["take"] = "true" }),
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.decline-{seat}"), "不获得拼点牌", [], [],
                    new Dictionary<string,string> { ["action"] = "pindian-claim", ["take"] = "false" })
            };
            SetPindianPrompt(frame, seat, DecisionKind.SkillModule, $"【{skill.Name}】：是否获得拼点牌？", choices, ids, [],
                new(binding.Source.SkillId, skill.Name, skill.Name + " · 获得拼点牌", skill.Description));
            return true;
        }
        ReplaceRuntimeTop(frame with { ClaimSeats = seats });
        return false;
    }

    private void ResolvePindianClaimChoice(PromptChoice selected)
    {
        var frame = (PindianFrame)_resolutionStack[^1];
        var seat = frame.ClaimSeats![frame.ClaimIndex];
        if (_pendingDecision?.PlayerSeat != seat || !_pendingDecision.Choices.Any(choice => choice.Id == selected.Id))
            throw new InvalidOperationException("Pindian claim does not match its published claimant.");
        ClearPendingDecision();
        if (selected.Parameters["take"] == "true")
        {
            foreach (var id in selected.Cards)
            {
                var card = _cardZones.CardsAt(CardLocation.Processing).Single(item => item.Id == id);
                MoveCard(card, CardLocation.Processing, CardLocation.Hand(seat), new CardMoveReason("program.pindian.claim"));
            }
        }
        frame = frame with { ClaimIndex = frame.ClaimIndex + 1 };
        ReplaceRuntimeTop(frame);
        if (!BeginPindianClaims(frame)) CompletePindian((PindianFrame)_resolutionStack[^1]);
    }
}
