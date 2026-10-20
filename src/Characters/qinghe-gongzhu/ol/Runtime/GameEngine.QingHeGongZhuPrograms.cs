namespace CardGame.Core;

internal interface IQingHeGongZhuProgramHost
{
    SkillProgramStepOutcome ChangjiEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome ZengouNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// 长姬 evidence: the committed ending-phase choice is public; the draw count,
// the discarded cards and the damage evidence ride one scalar record.
public sealed record ProgramChangjiEndingEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int EndingSeat, string Branch, IReadOnlyList<int> DiscardedCardIds, int DrawCount) : IGameEvent;

// 谮构 evidence: the paid cost, the nullified dodge and the gained entity are
// public once they move; one record carries the whole activation.
public sealed record ProgramZengouNullifiedEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int DodgerSeat, string Cost, int? PaidCardId, int? GainedCardId) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ChangjiDiscardState? ChangjiDiscard { get; init; }
}

// One prompt per discarded card; the receipt rides the owning program frame.
public sealed record ChangjiDiscardState(int EndingSeat, int OwnerSeat, int Remaining,
    IReadOnlyList<int> DiscardedCardIds);

public sealed partial class GameEngine
{

    private Card? LatestSettledDodgeEntity()
    {
        foreach (var movement in _cardMovements.AsEnumerable().Reverse())
        {
            if (movement.To != CardLocation.DiscardPile ||
                movement.Reason.Value != CardMoveReasons.ResponseFinished.Value)
                continue;
            var card = _cardZones.CardsAt(CardLocation.DiscardPile)
                .FirstOrDefault(item => item.Id == movement.CardId);
            if (card is null) continue;
            return GetAdvancedCard(card.Id).Kind == CardKind.Dodge ? card : null;
        }
        return null;
    }

    private IReadOnlyList<Card> NonBasicOwnedCards(int ownerSeat) =>
        _cardZones.CardsAt(CardLocation.Hand(ownerSeat))
            .Concat(_cardZones.CardsAt(CardLocation.Equipment(ownerSeat)))
            .Where(card => CardCatalog.Get(card.Kind).CategoryName != "基本牌")
            .ToArray();



    [ProgramCapability]
    private sealed class QingHeGongZhuProgramCapability(GameEngine engine) : IQingHeGongZhuProgramHost
    {
        public SkillProgramStepOutcome ChangjiEndingDamageChoice(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ChangjiProgramEndingDamageChoice(frame, effect);
        public SkillProgramStepOutcome ZengouNullifyDodge(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZengouProgramNullifyDodge(frame, effect);
    }
}
