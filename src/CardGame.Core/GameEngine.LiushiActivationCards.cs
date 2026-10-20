namespace CardGame.Core;

internal interface ILiushiTopdeckSelectionQuery : IProgramSelectionQuery
{ bool CanTopdeck(int ownerSeat, int cardId); }

[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 1283, SkillProgramEffectOp.UseVirtualCard)]
internal sealed class LiushiTopdeckSelectionFilter : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        if (!LiushiEffectContract.IsActivation(context.Activation)) return true;
        candidates.Cards = candidates.Cards.Where(id => queries.Get<ILiushiTopdeckSelectionQuery>().CanTopdeck(context.OwnerSeat, id)).ToArray();
        return candidates.Cards.Length > 0;
    }
}

[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 1283, SkillProgramEffectOp.UseVirtualCard)]
internal sealed class LiushiTopdeckSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        !LiushiEffectContract.IsActivation(request.Context.Activation) || request.Cards is [var card] &&
        queries.Get<ILiushiTopdeckSelectionQuery>().CanTopdeck(request.Context.OwnerSeat, card)
            ? null : new(CommandErrorCode.IllegalAction, "流矢材料必须能实际置于牌堆顶。");
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class LiushiTopdeckSelectionQuery(GameEngine engine) : ILiushiTopdeckSelectionQuery
    { public bool CanTopdeck(int ownerSeat, int cardId) => engine.CanTopdeckLiushiCard(ownerSeat, cardId); }

    private bool CanTopdeckLiushiCard(int ownerSeat, int cardId)
    {
        var card = GetHand(_players[ownerSeat]).Concat(GetEquipment(ownerSeat)).SingleOrDefault(c => c.Id == cardId);
        if (card is null) return false;
        var from = _cardZones.GetLocation(cardId);
        return !(card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment) &&
            !IsGuanduThunderChariotDeparture(card, from, CardLocation.DrawPile) &&
            NormalizeDiouCarrierDestination(card, from, CardLocation.DrawPile) == CardLocation.DrawPile;
    }
}
