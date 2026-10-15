namespace CardGame.Core;

public sealed partial class GameEngine
{
    private PindianPolicyClaims FreezeSimultaneousClaims(ProgramSkillFrame root,PindianResult result)
    {
        var pairCards = new[] { (Id: result.SourceCardId,Rank: result.SourceRank),(Id: result.OpponentCardId,Rank: result.OpponentRank) };
        var slashes = pairCards.Where(c => _cardZones.GetLocation(c.Id) == CardLocation.Processing && IsSlashCard(GetAdvancedCard(c.Id).Kind)).ToArray();
        var maximum = slashes.Select(c => c.Rank).DefaultIfEmpty(0).Max(); var maximumIds = slashes.Where(c => c.Rank == maximum).Select(c => c.Id).ToArray();
        var claims = new List<PindianPolicyClaim>();
        foreach (var seat in new[] { result.SourceSeat,result.OpponentSeat })
            foreach (var kind in new[] { SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins,SkillProgramCardPolicyKind.PindianClaim,SkillProgramCardPolicyKind.PindianMaximumSlashClaim })
            {
                var binding = CardPolicies(_players[seat],kind).FirstOrDefault(); if (binding.Source is null) continue;
                var ids = kind == SkillProgramCardPolicyKind.PindianMaximumSlashClaim ? maximumIds :
                    kind == SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins ? seat == result.SourceSeat && !result.WonBy(result.OpponentSeat) ? pairCards.Select(c => c.Id).ToArray() : [] :
                    new[] { result.WonBy(seat) ? result.SourceRank < result.OpponentRank ? result.SourceCardId : result.OpponentCardId : result.CardOf(seat) };
                claims.Add(new(seat,new(binding.Source.SkillId,binding.Policy.Id,seat,binding.Source.SkillInstanceId),kind,Array.AsReadOnly(ids.ToArray())));
            }
        return new(Array.AsReadOnly(claims.ToArray()));
    }
    private int[] AvailableSimultaneousClaimCards(PindianPolicyClaim claim) => PindianPolicySourceLive(claim.OwnerSeat,claim.Source,claim.Kind)
        ? claim.CardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray() : [];
    private bool OfferSimultaneousClaim(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!; var claims = r.Claims!;
        while (claims.Index < claims.Claims.Count)
        {
            if (AvailableSimultaneousClaimCards(claims.Claims[claims.Index]).Length > 0)
            {
                ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Stage = SimultaneousPindianStage.Claiming, Claims = claims } });
                PublishSimultaneousChoice(f); return true;
            }
            claims = claims with { Index = claims.Index + 1 };
        }
        ReplaceRuntimeTop(f with { SimultaneousPindian = r with { Claims = claims } }); return false;
    }
    private void ResolveSimultaneousClaim(ProgramSkillFrame f,PromptChoice choice)
    {
        var r = f.SimultaneousPindian!; var claim = r.Claims!.Claims[r.Claims.Index]; var taken = choice.Parameters["branch"] == "claim";
        var available = AvailableSimultaneousClaimCards(claim);
        if (available.Length == 0 || taken && !choice.Cards.SequenceEqual(available) || !taken && choice.Cards.Count != 0)
            throw new InvalidOperationException("A simultaneous claim lost its exact frozen public entities or live policy source.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { SimultaneousPindian = r with { Stage = SimultaneousPindianStage.ClaimMovementChildren, PaymentSeat = claim.OwnerSeat,
            PaymentKind = taken ? "claim" : "claim-decline", MovementBefore = before, MovementAfter = before }, PendingMovementContinuation = new(claim.OwnerSeat,0,null) });
        if (taken) foreach (var id in available) MoveCard(GetAdvancedCard(id),CardLocation.Processing,CardLocation.Hand(claim.OwnerSeat),new("program.pindian.claim"));
        f = GetActiveProgramFrame(f.Id); r = f.SimultaneousPindian! with { MovementAfter = _movementSequence }; ReplaceRuntimeTop(f = f with { SimultaneousPindian = r });
        AdvanceEventRulesAndQueueFact(new SimultaneousPindianClaimPaidEvent(f.Id,r.ComparisonIndex,r.Claims!.Index,claim.Source,taken,choice.Cards,before,r.MovementAfter));
        if (!DrainSimultaneousChildren(f)) AdvanceRuntimeProgram(f.Id);
    }
    private void AssertSimultaneousClaims(ProgramSkillFrame f)
    {
        var r = f.SimultaneousPindian!;
        if (r.Claims is not { } claims) return;
        var result = SimultaneousResult(f);
        if (!r.ComparisonIssued || claims.Index < 0 || claims.Index > claims.Claims.Count || claims.Claims is not System.Collections.ObjectModel.ReadOnlyCollection<PindianPolicyClaim> ||
            claims.Claims.Any(c => c.OwnerSeat != result.SourceSeat && c.OwnerSeat != result.OpponentSeat || c.Source.OwnerSeat != c.OwnerSeat ||
                c.Kind is not (SkillProgramCardPolicyKind.PindianClaim or SkillProgramCardPolicyKind.PindianClaimAllWhenSourceWins or SkillProgramCardPolicyKind.PindianMaximumSlashClaim) ||
                c.CardIds is not System.Collections.ObjectModel.ReadOnlyCollection<int> || c.CardIds.Distinct().Count() != c.CardIds.Count ||
                c.CardIds.Any(id => id != result.SourceCardId && id != result.OpponentCardId)) ||
            CompleteProgramEventHistory().OfType<SimultaneousPindianClaimsIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.ComparisonIndex == r.ComparisonIndex &&
                e.Claims.Index == 0 && e.Claims.Claims.Count == claims.Claims.Count && e.Claims.Claims.Zip(claims.Claims).All(p => p.First.Source == p.Second.Source &&
                    p.First.OwnerSeat == p.Second.OwnerSeat && p.First.Kind == p.Second.Kind && p.First.CardIds.SequenceEqual(p.Second.CardIds))) != 1)
            throw new InvalidOperationException("A simultaneous result claim lost its exact once-issued typed public policy cursor.");
        if (r.Stage is SimultaneousPindianStage.Claiming or SimultaneousPindianStage.ClaimMovementChildren && claims.Index >= claims.Claims.Count)
            throw new InvalidOperationException("A simultaneous claim outlived its exact current candidate.");
    }
}
