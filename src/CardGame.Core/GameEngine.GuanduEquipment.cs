namespace CardGame.Core;

public sealed record GuanduChariotCreatedEvent(long ProgramFrameId, int OwnerSeat, int CardId) : IGameEvent;
public sealed record GuanduChariotDestroyedEvent(int CardId, int MovementSequence, int FormerOwnerSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private static string? GuanduEquipmentSkillGrantId(Card card) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_GuanduEquipment.GuanduEquipmentSkillGrantId(card);
    private bool ResolveGuanduEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (GuanduEquipmentSkillGrantId(card) is not { } grant) return false;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } old }) _players[old].SkillGrants.RemoveGrant(grant);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } target } && _contentRegistry.Skills.ContainsKey("guandu:thunder-chariot"))
            _players[target].SkillGrants.Grant(new(grant, "guandu:thunder-chariot", grant, grant));
        return true;
    }
    private static bool IsGuanduThunderChariotDeparture(Card card, CardLocation from, CardLocation to) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_GuanduEquipment.IsGuanduThunderChariotDeparture(card,from,to);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 44002)]
    private void RecordGuanduChariotDestruction(IGameEvent payload)
    {
        if (payload is not CardMovedEvent { CardKind: CardKind.ThunderChariot,
            From: { Zone: CardZoneKind.Equipment, OwnerSeat: { } owner }, To: { Zone: CardZoneKind.OutsideGame } } moved) return;
        var movement = _cardMovements.Last(m => m.CardId == moved.CardId);
        if (!ProgramEventHistory<GuanduChariotDestroyedEvent>().Any(e => e.MovementSequence == movement.Sequence))
            AdvanceEventRulesAndQueueFact(new GuanduChariotDestroyedEvent(moved.CardId, movement.Sequence, owner));
    }
    private bool IsGuanduDestroyedChariotMaterial(long useId, int cardId) => cardId > 0 && LifecycleCardUse(useId)?.Action is { } action &&
        _cardZones.GetLocation(cardId) == CardLocation.OutsideGame &&
        action.PhysicalCards.Any(material => material.CardId == cardId && material.CardKind == CardKind.ThunderChariot &&
            material.From is { Zone: CardZoneKind.Equipment, OwnerSeat: { } owner } &&
            ProgramEventHistory<GuanduChariotDestroyedEvent>().Any(e => e.CardId == cardId && e.FormerOwnerSeat == owner &&
                _cardMovements.Any(m => m.CardId == cardId && m.Sequence == e.MovementSequence &&
                    m.From == material.From && m.To == CardLocation.OutsideGame)));

    private void ContinueGuanduPolu(ProgramSkillFrame f)
    {
        if (f.WindowContext!.Window == SkillProgramTriggerWindow.AfterDamageApplied)
        { DrawGuanduGeneral(f, f.OwnerSeat, 1, "finish"); return; }
        var owner = _players[f.OwnerSeat]; f = GuanduStage(f, "finish");
        if (owner.IsAlive && !GetEquipment(owner).Any(c => c.Kind == CardKind.ThunderChariot) && owner.EquipmentSlotCapacity(EquipmentSlot.Weapon) > 0)
        {
            var id = _cardZones.CreateDiagnostics().Select(c => c.CardId).DefaultIfEmpty().Max() + 1;
            var card = new Card(id, CardKind.ThunderChariot, Suit.Diamond, 9);
            _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(id);
            AdvanceEventRulesAndQueueFact(new GuanduChariotCreatedEvent(f.Id, f.OwnerSeat, card.Id));
            PlaceAdvancedEquipment(card, f.OwnerSeat);
        }
        RunGuanduGeneral(f.Id);
    }
    private Card[] GuanduChariotDiscardable(int actor, int target) => GetEquipment(_players[target])
        .Where(c => EquipmentCatalog.Get(c.Kind).Slot is EquipmentSlot.Armor or EquipmentSlot.DefensiveHorse)
        .Where(c => !IsForeignEquipmentDiscardPrevented(actor, c, CardLocation.Equipment(target), OwnedCardMoveIntent.Discard)).ToArray();
    private void ContinueGuanduThunderChariot(ProgramSkillFrame f)
    {
        var target = f.WindowContext?.TargetSeat ?? -1;
        if (!IsValidPlayerSeat(target) || target == f.OwnerSeat) throw new InvalidOperationException("The chariot lost its actual other damaged target.");
        var cards = GuanduChariotDiscardable(f.OwnerSeat, target);
        f = GuanduStage(f, "finish");
        if (cards.Length != 0)
            MoveLogicalDiscardCards(f.OwnerSeat, f.OwnerSeat, cards, CardLocation.Equipment(target), new("activity.guandu.chariot.discard"));
        RunGuanduGeneral(f.Id);
    }
}
