namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 26620, SkillProgramEffectOp.ResolveWeifuJudgment)]
internal sealed class WeifuCostFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext c, Card card, IProgramSelectionQueries q)
    {
        var state = q.Get<IProgramSelectionCardState>(); var from = state.Location(card.Id);
        return from.OwnerSeat == c.OwnerSeat && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && !card.IsGeneralWeapon &&
            !state.IsSelfDiscardForbidden(c.OwnerSeat, card, from) && !state.IsForeignEquipmentDiscardPrevented(c.OwnerSeat, card, from) &&
            !(from.Zone == CardZoneKind.Equipment && state.IsActiveSourceEquipment(c.OwnerSeat, c.SkillId, card));
    }
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 26620, SkillProgramEffectOp.ResolveWeifuJudgment)]
internal sealed class WeifuCostValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest r, IProgramSelectionQueries q)
    {
        var source = q.Get<IProgramSelectionSourceQuery>();
        var card = r.Cards is [var id] ? source.Hand(r.Context.OwnerSeat).Concat(source.Equipment(r.Context.OwnerSeat)).SingleOrDefault(c => c.Id == id) : null;
        return card is null || r.Targets.Count != 0 || !new WeifuCostFilter().Allows(r.Context, card, q)
            ? new(CommandErrorCode.InvalidCard, "威抚须弃置一张可弃置的自有手牌或装备牌。") : null;
    }
}

internal interface IWeifuSelectionAiQuery : IProgramSelectionQuery
{ int UnusedGrants(int owner, SkillProgramCardCategory category); }

[ProgramSelectionModule(ProgramSelectionStage.AiHint, 26620, SkillProgramEffectOp.ResolveWeifuJudgment)]
internal sealed class WeifuSelectionAi : IProgramSelectionAiDecorator
{
    public SkillProgramAiHint Decorate(ProgramSelectionContext c, SkillProgramAiHint hint, IProgramSelectionQueries q)
    {
        var source = q.Get<IProgramSelectionSourceQuery>(); var cards = source.Hand(c.OwnerSeat);
        var self = source.Participants().Single(p => p.Seat == c.OwnerSeat);
        var basic = q.Get<IWeifuSelectionAiQuery>().UnusedGrants(c.OwnerSeat, SkillProgramCardCategory.Basic);
        var trick = q.Get<IWeifuSelectionAiQuery>().UnusedGrants(c.OwnerSeat, SkillProgramCardCategory.Trick);
        var basicUse = cards.Any(card => card.Kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash && source.Participants().Any(p => p.IsAlive && p.Seat != c.OwnerSeat && source.CanUseSlash(c.OwnerSeat, p.Seat, [card], true)) ||
            card.Kind == CardKind.Peach && self.Hp < self.MaxHp || card.Kind == CardKind.Alcohol && !source.IsUseForbidden(c.OwnerSeat, CardKind.Alcohol));
        var trickUse = cards.Any(card => CardCatalog.Get(card.Kind).CategoryName == "锦囊牌" && card.Kind != CardKind.Nullification &&
            source.CanUseOrdinaryTrick(c.OwnerSeat, card.Kind, [card.Id]));
        // The shared action scorer already charges the selected card's keep value.
        // Remove its generic active-skill baseline; value only public future uses,
        // an unknown matching-judge draw prior and diminishing outstanding grants.
        var benefit = (basicUse || trickUse ? 3d : 0d) + (basicUse ? 20d / (1 + basic) : 0d) + (trickUse ? 16d / (1 + trick) : 0d) - 4d * (basic + trick);
        return hint with { ValueAdjustment = benefit - 12d };
    }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WeifuSelectionAiQuery(GameEngine e) : IWeifuSelectionAiQuery
    {
        public int UnusedGrants(int owner, SkillProgramCardCategory category) => e.ActiveWeifuGrants(owner,
            category == SkillProgramCardCategory.Basic ? CardKind.Slash : CardKind.DrawTwo).Count;
    }
}
