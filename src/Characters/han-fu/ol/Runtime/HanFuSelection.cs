namespace CardGame.Core;
[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard,27020,SkillProgramEffectOp.ResolveShuziExchange)]
internal sealed class ShuziMaterialFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext c,Card card,IProgramSelectionQueries q)
    { var from=q.Get<IProgramSelectionCardState>().Location(card.Id); return !card.IsGeneralWeapon && from.OwnerSeat==c.OwnerSeat && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment; }
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits,27020,SkillProgramEffectOp.ResolveShuziExchange)]
internal sealed class ShuziMaterialValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest r,IProgramSelectionQueries q)
    {
        var source=q.Get<IProgramSelectionSourceQuery>(); var owned=source.Hand(r.Context.OwnerSeat).Concat(source.Equipment(r.Context.OwnerSeat)).ToArray();
        return r.Cards.Count!=2 || r.Cards.Distinct().Count()!=2 || r.Targets.Count!=1 || r.Cards.Any(id=>owned.SingleOrDefault(c=>c.Id==id) is not { } card || !new ShuziMaterialFilter().Allows(r.Context,card,q))
            ? new(CommandErrorCode.InvalidCard,"束辎须选择两张真实自有手牌或装备牌及一名角色。") : null;
    }
}
