namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsXieweiMultiTargetAttack(FangtianHalberdHandle pending, CardAttackHandle attack,
        IReadOnlyList<Card> processing) =>
        LifecycleCardUse(pending.ResolutionId) is { PhysicalCardIds: { } materials } use &&
        IsXieweiMultiTargetUse(use) && attack.ResolutionId == use.Id &&
        attack.SourceSeat == pending.SourceSeat && attack.SourceSeat == use.SourceSeat &&
        attack.EffectiveCardKind == pending.EffectiveCardKind && attack.EffectiveCardKind == use.CardKind &&
        attack.Card == pending.Card && pending.Card.Id == use.CardId &&
        use.TargetIndex == pending.TargetIndex && pending.TargetIndex < pending.TargetSeats.Count &&
        attack.TargetSeat == GetFangtianEffectiveTarget(pending, pending.TargetIndex) &&
        attack.PhysicalCards.Select(card => card.Id).SequenceEqual(materials) &&
        materials.All(id => IsXieweiRemovedMaterial(use.Id, id)) && processing.Count == 0;

    private Suit? ProgramMultiCardSuit(CharacterState owner, ProgramMultiCardViewAsSelection selection) =>
        IsXieweiSelection(selection) || IsWushuangJinzhiSelection(selection) ? Suit.None : PhysicalGroupSuit(owner, selection.Cards);

    private bool? ProgramMultiCardColor(CharacterState owner, ProgramMultiCardViewAsSelection selection) =>
        IsXieweiSelection(selection) || IsWushuangJinzhiSelection(selection) ? null : PhysicalGroupColor(owner, selection.Cards);
}
