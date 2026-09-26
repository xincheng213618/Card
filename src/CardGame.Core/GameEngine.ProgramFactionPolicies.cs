namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record FactionResponsePolicySource(string SkillId, string SkillInstanceId,
        string PolicyId, string FactionId, CardKind RequiredKind);

    private FactionResponsePolicySource? GetFactionResponsePolicy(CharacterState owner, CardKind requiredKind) =>
        CardPolicies(owner, SkillProgramCardPolicyKind.FactionResponseRequest, requiredKind: requiredKind)
            .Where(item => GetFactionProviderSeats(owner.Seat, item.Policy.FactionId!).Count > 0)
            .Select(item => new FactionResponsePolicySource(item.Source.SkillId, item.Source.SkillInstanceId,
                item.Policy.Id, item.Policy.FactionId!, requiredKind)).FirstOrDefault();

    private string FactionPolicyName(FactionResponsePolicySource source) =>
        _contentRegistry!.GetSkill(source.SkillId).Name;

    private IReadOnlyList<int> GetFactionDefenseCandidateSeats(int ownerSeat) =>
        GetFactionResponsePolicy(_players[ownerSeat], CardKind.Dodge) is { } source
            ? GetFactionProviderSeats(ownerSeat, source.FactionId) : [];

    private IReadOnlyList<int> GetFactionSlashCandidateSeats(int ownerSeat) =>
        GetFactionResponsePolicy(_players[ownerSeat], CardKind.Slash) is { } source
            ? GetFactionProviderSeats(ownerSeat, source.FactionId) : [];
}
