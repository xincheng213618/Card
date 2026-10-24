namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.CardBeforeDiscard, 10,
    SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash, SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial)]
internal sealed class WangYunSelectionCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries)
    {
        if (context.Features.HasOperation(SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash))
        {
            var state = queries.Get<IProgramSelectionCardState>();
            return state.Location(card.Id) == CardLocation.Hand(context.OwnerSeat) &&
                !state.IsSelfDiscardForbidden(context.OwnerSeat, card, CardLocation.Hand(context.OwnerSeat));
        }
        return card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash ||
            EquipmentCatalog.IsEquipment(card.Kind);
    }
}
