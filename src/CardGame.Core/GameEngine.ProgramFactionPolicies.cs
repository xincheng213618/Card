namespace CardGame.Core;

public sealed partial class GameEngine
{


    private FactionResponsePolicySource? GetFactionResponsePolicy(CharacterState owner, CardKind requiredKind) =>
        CardPolicies(owner, SkillProgramCardPolicyKind.FactionResponseRequest, requiredKind: requiredKind)
            .Where(item => GetFactionProviderSeats(owner.Seat, item.Policy.FactionId!).Count > 0)
            .Where(item => item.Policy.DiscardCost == 0 || GetHand(owner).Count + GetEquipment(owner).Count >= item.Policy.DiscardCost)
            .Select(item => new FactionResponsePolicySource(item.Source.SkillId, item.Source.SkillInstanceId,
                item.Policy.Id, item.Policy.FactionId!, requiredKind, item.Policy.DiscardCost, item.Policy.ProviderDrawCount)).FirstOrDefault();

    private string FactionPolicyName(FactionResponsePolicySource source) =>
        _contentRegistry!.GetSkill(source.SkillId).Name;

    private IReadOnlyList<int> GetFactionDefenseCandidateSeats(int ownerSeat) =>
        GetFactionResponsePolicy(_players[ownerSeat], CardKind.Dodge) is { } source
            ? GetFactionProviderSeats(ownerSeat, source.FactionId) : [];

    private IReadOnlyList<int> GetFactionSlashCandidateSeats(int ownerSeat) =>
        GetFactionResponsePolicy(_players[ownerSeat], CardKind.Slash) is { } source
            ? GetFactionProviderSeats(ownerSeat, source.FactionId) : [];
}
