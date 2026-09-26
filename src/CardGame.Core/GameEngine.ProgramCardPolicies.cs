namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IEnumerable<(IndexedSkillProgramInstance Source, SkillProgramCardPolicy Policy)> CardPolicies(
        CharacterState owner,
        SkillProgramCardPolicyKind kind,
        CardKind? effectiveKind = null,
        CardKind? requiredKind = null)
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
                    policy.Condition.Evaluate(context))
                .Select(policy => (instance, policy)));
    }

    private bool HasCardPolicy(CharacterState owner, SkillProgramCardPolicyKind kind,
        CardKind? effectiveKind = null) =>
        CardPolicies(owner, kind, effectiveKind).Any();

    private int GetProgramRequiredResponseCount(CharacterState owner, int sourceSeat,
        CardKind incomingKind, CardKind requiredKind) =>
        sourceSeat == owner.Seat
            ? Math.Max(1, CardPolicies(owner, SkillProgramCardPolicyKind.MinimumResponseCount,
                    incomingKind, requiredKind).Select(item => item.Policy.Value).DefaultIfEmpty(1).Max())
            : 1;

    private Suit GetProgramEffectiveSuit(CharacterState owner, Card card)
    {
        var suit = card.Suit;
        foreach (var item in CardPolicies(owner, SkillProgramCardPolicyKind.RewriteSuit))
            if (item.Policy.InputSuit == suit) suit = item.Policy.OutputSuit!.Value;
        return suit;
    }
}
