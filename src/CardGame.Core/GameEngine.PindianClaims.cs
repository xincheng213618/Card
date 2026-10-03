namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool BeginPindianClaims(PindianFrame frame)
    {
        if (HasMaximumSlashPindianClaim(frame)) return BeginMaximumSlashPindianClaims(frame);
        var result = frame.Result ?? throw new InvalidOperationException("Pindian claim requires a revealed result.");
        var seats = frame.ClaimSeats ?? new[] { result.SourceSeat, result.OpponentSeat }
            .Where(seat => CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianClaim).Any()).ToArray();
        while (frame.ClaimIndex < seats.Count)
        {
            var seat = seats[frame.ClaimIndex];
            var id = result.WonBy(seat)
                ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId
                : result.CardOf(seat);
            var binding = CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianClaim).FirstOrDefault();
            if (!_players[seat].IsAlive || binding.Source is null || _cardZones.GetLocation(id) != CardLocation.Processing)
            { frame = frame with { ClaimSeats = seats, ClaimIndex = frame.ClaimIndex + 1 }; continue; }
            frame = frame with { ClaimSeats = seats, PindianStep = PindianStep.ClaimResult };
            ReplaceRuntimeTop(frame);
            var skill = _contentRegistry.GetSkill(binding.Source.SkillId);
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(item => item.Id == id);
            var choices = new[]
            {
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.claim-{seat}-{id}"), $"获得【{card.DisplayName}】", [id], [],
                    new Dictionary<string,string> { ["action"] = "pindian-claim", ["take"] = "true" }),
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.decline-{seat}"), "不获得拼点牌", [], [],
                    new Dictionary<string,string> { ["action"] = "pindian-claim", ["take"] = "false" })
            };
            SetPindianPrompt(frame, seat, DecisionKind.SkillModule, $"【{skill.Name}】：是否获得拼点牌？", choices, [id], [],
                new(binding.Source.SkillId, skill.Name, skill.Name + " · 获得拼点牌", skill.Description));
            return true;
        }
        ReplaceRuntimeTop(frame with { ClaimSeats = seats });
        return false;
    }

    private void ResolvePindianClaimChoice(PromptChoice selected)
    {
        var frame = (PindianFrame)_resolutionStack[^1];
        if (frame.PolicyClaims is not null) { ResolveMaximumSlashPindianClaim(frame, selected); return; }
        var seat = frame.ClaimSeats![frame.ClaimIndex];
        if (_pendingDecision?.PlayerSeat != seat || !_pendingDecision.Choices.Any(choice => choice.Id == selected.Id))
            throw new InvalidOperationException("Pindian claim does not match its published claimant.");
        ClearPendingDecision();
        if (selected.Parameters["take"] == "true")
        {
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(item => item.Id == selected.Cards.Single());
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(seat), new CardMoveReason("program.pindian.claim"));
        }
        frame = frame with { ClaimIndex = frame.ClaimIndex + 1 };
        ReplaceRuntimeTop(frame);
        if (!BeginPindianClaims(frame)) CompletePindian((PindianFrame)_resolutionStack[^1]);
    }
}
