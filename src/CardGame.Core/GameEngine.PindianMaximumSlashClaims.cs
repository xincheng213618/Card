namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMaximumSlashPindianClaim(PindianFrame frame) => frame.PolicyClaims is not null ||
        new[] { frame.SourceSeat, frame.OpponentSeat!.Value }.Any(seat =>
            CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianMaximumSlashClaim).Any());

    private PindianPolicyClaims FreezePindianPolicyClaims(PindianFrame frame)
    {
        var result = frame.Result!;
        var cards = new[] { (Id: result.SourceCardId, Rank: result.SourceRank), (Id: result.OpponentCardId, Rank: result.OpponentRank) };
        var slashes = cards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing &&
            IsSlashCard(_cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == c.Id).Kind)).ToArray();
        var maximum = slashes.Length == 0 ? 0 : slashes.Max(c => c.Rank);
        var maximumIds = slashes.Where(c => c.Rank == maximum).Select(c => c.Id).ToArray();
        var claims = new List<PindianPolicyClaim>();
        foreach (var seat in new[] { result.SourceSeat, result.OpponentSeat })
        {
            // Participant order is unchanged. Within a participant, the existing claim precedes the new one.
            foreach (var kind in new[] { SkillProgramCardPolicyKind.PindianClaim, SkillProgramCardPolicyKind.PindianMaximumSlashClaim })
            {
                var binding = CardPolicies(_players[seat], kind).FirstOrDefault();
                if (binding.Source is null) continue;
                var ids = kind == SkillProgramCardPolicyKind.PindianMaximumSlashClaim ? maximumIds :
                    new[] { result.WonBy(seat) ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId : result.CardOf(seat) };
                claims.Add(new(seat, new(binding.Source.SkillId, binding.Policy.Id, seat, binding.Source.SkillInstanceId), kind,
                    Array.AsReadOnly(ids.ToArray())));
            }
        }
        return new(Array.AsReadOnly(claims.ToArray()));
    }

    private bool BeginMaximumSlashPindianClaims(PindianFrame frame)
    {
        var state = frame.PolicyClaims ?? FreezePindianPolicyClaims(frame);
        while (state.Index < state.Claims.Count)
        {
            var claim = state.Claims[state.Index];
            var ids = claim.CardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
            if (ids.Length == 0 || !PindianPolicySourceLive(claim.OwnerSeat, claim.Source, claim.Kind))
            { state = state with { Index = state.Index + 1 }; continue; }
            frame = frame with { PolicyClaims = state, PindianStep = PindianStep.ClaimResult,
                ClaimSeats = Array.AsReadOnly(state.Claims.Select(c => c.OwnerSeat).ToArray()), ClaimIndex = state.Index };
            ReplaceRuntimeTop(frame);
            var skill = _contentRegistry.GetSkill(claim.Source.SkillId);
            var choices = new[]
            {
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.policy-claim-{state.Index}"), $"获得拼点牌（{ids.Length}张）", ids, [],
                    new Dictionary<string, string> { ["action"] = "pindian-claim", ["take"] = "true" }),
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.policy-decline-{state.Index}"), "不获得拼点牌", [], [],
                    new Dictionary<string, string> { ["action"] = "pindian-claim", ["take"] = "false" })
            };
            SetPindianPrompt(frame, claim.OwnerSeat, DecisionKind.SkillModule, $"【{skill.Name}】：是否获得拼点牌？", choices, ids, [],
                new(claim.Source.SkillId, skill.Name, skill.Name + " · 获得拼点牌", skill.Description));
            return true;
        }
        ReplaceRuntimeTop(frame with { PolicyClaims = state });
        return false;
    }

    private void ResolveMaximumSlashPindianClaim(PindianFrame frame, PromptChoice selected)
    {
        var state = frame.PolicyClaims!;
        var claim = state.Claims[state.Index];
        if (_pendingDecision?.PlayerSeat != claim.OwnerSeat || !_pendingDecision.Choices.Any(c => c.Id == selected.Id))
            throw new InvalidOperationException("Maximum Slash claim lost its published owner.");
        ClearPendingDecision();
        // Frozen identities only: if another claimant already obtained one, never replace it with a lower Slash.
        if (selected.Parameters["take"] == "true" && PindianPolicySourceLive(claim.OwnerSeat, claim.Source, claim.Kind))
            foreach (var id in selected.Cards.Where(id => claim.CardIds.Contains(id) && _cardZones.GetLocation(id) == CardLocation.Processing))
            {
                var card = _cardZones.CardsAt(CardLocation.Processing).Single(c => c.Id == id);
                MoveCard(card, CardLocation.Processing, CardLocation.Hand(claim.OwnerSeat), new CardMoveReason("program.pindian.claim"));
            }
        frame = frame with { PolicyClaims = state with { Index = state.Index + 1 }, ClaimIndex = state.Index + 1 };
        ReplaceRuntimeTop(frame);
        if (!BeginMaximumSlashPindianClaims(frame)) CompletePindian((PindianFrame)_resolutionStack[^1]);
    }

    private void AssertPindianPolicyClaims(PindianFrame frame)
    {
        if (frame.PolicyClaims is not { } state) return;
        if (state.Claims is not System.Collections.ObjectModel.ReadOnlyCollection<PindianPolicyClaim> ||
            state.Index < 0 || state.Index >= state.Claims.Count ||
            state.Claims.Any(c => c.CardIds is not System.Collections.ObjectModel.ReadOnlyCollection<int> ||
                c.CardIds.Distinct().Count() != c.CardIds.Count ||
                c.CardIds.Any(id => id != frame.Result!.SourceCardId && id != frame.Result.OpponentCardId)) ||
            frame.ClaimIndex != state.Index)
            throw new InvalidOperationException("Pindian maximum claim lost its frozen public identities.");
    }
}
