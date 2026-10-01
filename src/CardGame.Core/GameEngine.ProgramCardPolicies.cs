namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(IndexedSkillProgramInstance Source, SkillProgramCardPolicy Policy)> CardPolicies(
        CharacterState owner,
        SkillProgramCardPolicyKind kind,
        CardKind? effectiveKind = null,
        CardKind? requiredKind = null,
        bool? eventTargetIsFemale = null,
        bool? eventSourceIsFemale = null)
    {
        var context = CreateSkillContext(owner);
        return GetSkillBindingShard(owner).ProgramInstances
            .OrderBy(instance => instance.SkillId, StringComparer.Ordinal)
            .ThenBy(instance => instance.SkillInstanceId, StringComparer.Ordinal)
            .SelectMany(instance => instance.Program.CardPolicies
                .Where(policy => policy.Kind == kind &&
                    (policy.OwnerRole is null || policy.OwnerRole == owner.Role) &&
                    (policy.CardKinds.Count == 0 ||
                     effectiveKind is { } card && policy.CardKinds.Contains(card)) &&
                    (policy.RequiredCardKinds.Count == 0 ||
                     requiredKind is { } response && policy.RequiredCardKinds.Contains(response)) &&
                    policy.Condition.Evaluate(context, null, eventTargetIsFemale,
                        eventSourceIsFemale))
                .Select(policy => (instance, policy)));
    }

    private bool HasCardPolicy(CharacterState owner, SkillProgramCardPolicyKind kind,
        CardKind? effectiveKind = null) =>
        CardPolicies(owner, kind, effectiveKind).Any();

    private bool IsNearbyTargetResponseProhibited(int sourceSeat, int responderSeat,
        CardKind incomingKind, IReadOnlyList<int> targetSeats)
    {
        if ((!IsSlashCard(incomingKind) && !IsOrdinaryTrick(incomingKind)) ||
            sourceSeat == responderSeat || !targetSeats.Contains(responderSeat) ||
            !_players[sourceSeat].IsAlive || !_players[responderSeat].IsAlive)
            return false;

        var distance = GetCombatDistance(sourceSeat, responderSeat);
        return CardPolicies(_players[sourceSeat],
                SkillProgramCardPolicyKind.ProhibitNearbyTargetResponse, incomingKind)
            .Any(item => distance <= item.Policy.Value);
    }

    private bool IsSuitSlashResponseProhibited(CardAttackHandle attack)
    {
        if (attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            attack.CardUserSeat == attack.TargetSeat || attack.PhysicalCards.Count == 0)
            return false;
        var source = _players[attack.CardUserSeat];
        return CardPolicies(source, SkillProgramCardPolicyKind.ProhibitTargetSlashResponseBySuit, kind)
            .Any(item => attack.PhysicalCards.Any(card =>
                EffectiveSuit(source, card) == item.Policy.InputSuit));
    }

    private int GetProgramRequiredResponseCount(CharacterState owner, int sourceSeat, int responderSeat,
        CardKind incomingKind, CardKind requiredKind)
    {
        // Roulin's two directions share the response-count query: an attacker-side
        // MinimumResponseCount policy keys on the attack target's gender while a
        // defender-side MinimumResponseCountAsTarget policy keys on the attacker's.
        // The max (never a product) matches the Wushuang aggregation precedent.
        var attackerSide = sourceSeat == owner.Seat
            ? CardPolicies(owner, SkillProgramCardPolicyKind.MinimumResponseCount,
                    incomingKind, requiredKind,
                    eventTargetIsFemale: _players[responderSeat].Gender == GeneralGender.Female,
                    eventSourceIsFemale: _players[sourceSeat].Gender == GeneralGender.Female)
                .Select(item => item.Policy.Value).DefaultIfEmpty(1).Max()
            : 1;
        var targetSide = CardPolicies(_players[responderSeat],
                SkillProgramCardPolicyKind.MinimumResponseCountAsTarget,
                incomingKind, requiredKind,
                eventTargetIsFemale: _players[responderSeat].Gender == GeneralGender.Female,
                eventSourceIsFemale: _players[sourceSeat].Gender == GeneralGender.Female)
            .Select(item => item.Policy.Value).DefaultIfEmpty(0).Max();
        return Math.Max(attackerSide, targetSide);
    }

    private Suit GetProgramEffectiveSuit(CharacterState owner, Card card)
    {
        var suit = card.Suit;
        foreach (var item in CardPolicies(owner, SkillProgramCardPolicyKind.RewriteSuit))
            if (item.Policy.InputSuit == suit) suit = item.Policy.OutputSuit!.Value;
        return suit;
    }
}
