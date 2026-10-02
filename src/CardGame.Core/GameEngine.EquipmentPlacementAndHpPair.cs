namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Pure actual card/target eligibility: placement is neither Use nor Discard.
    private bool CanPlaceOwnedEquipment(int ownerSeat, int targetSeat, Card card, CardLocation source) =>
        IsValidPlayerSeat(ownerSeat) && IsValidPlayerSeat(targetSeat) &&
        _players[ownerSeat].IsAlive && _players[targetSeat].IsAlive &&
        source.OwnerSeat == ownerSeat && source.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        _cardZones.GetLocation(card.Id) == source && EquipmentCatalog.IsEquipment(card.Kind) &&
        source != CardLocation.Equipment(targetSeat) && !_players[targetSeat].EquipmentAreaAbolished &&
        _players[targetSeat].EquipmentSlotCapacity(EquipmentCatalog.Get(card.Kind).Slot) > 0;

    private bool CanPlaceActivationEquipment(int ownerSeat, int targetSeat, int cardId) =>
        CanPlaceOwnedEquipment(ownerSeat, targetSeat, GetAdvancedCard(cardId), _cardZones.GetLocation(cardId));

    private SkillProgramStepOutcome PlaceProgramSelectedEquipment(ProgramSkillFrame frame, int targetSeat, string bind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var source = GetProgramCardSet(active, bind);
        if (source.CardIds.Count != 1 || !source.CardIds.SequenceEqual(active.SelectedCardIds))
            throw new InvalidOperationException("Equipment placement requires its one captured activation cost.");
        var card = GetAdvancedCard(source.CardIds[0]);
        var from = source.SourceLocations.Single();
        if (!CanPlaceOwnedEquipment(frame.OwnerSeat, targetSeat, card, from) ||
            active.SelectedCardPayment is not null || active.SelectedCardPaymentResult is not null)
            throw new InvalidOperationException("The captured equipment placement cost is no longer legal.");
        ReplaceRuntimeTop(active with
        {
            SelectedCardPayment = new(active.InstructionIndex, SkillProgramEffectOp.PlaceSelectedEquipment,
                Array.AsReadOnly(source.CardIds.ToArray()), targetSeat) { PlacementSourceLocation = from }
        });
        var slot = EquipmentCatalog.Get(card.Kind).Slot;
        var equipped = GetEquipment(_players[targetSeat]).Where(item => EquipmentCatalog.Get(item.Kind).Slot == slot).ToArray();
        Card? replaced = equipped.Length >= _players[targetSeat].EquipmentSlotCapacity(slot) ? equipped[0] : null;
        if (replaced is not null)
        {
            CopyFirstReplacedWeapon(card, replaced);
            MoveCard(replaced, CardLocation.Equipment(targetSeat), CardLocation.DiscardPile, CardMoveReasons.EquipmentReplace);
        }
        MoveCard(card, from, CardLocation.Equipment(targetSeat), CardMoveReasons.EquipmentEnter);
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(frame.Id, targetSeat, slot, card.Id, card.Kind, replaced?.Id));
        AddLog("EquipmentChanged", $"{_players[frame.OwnerSeat].Name} 将【{card.DisplayName}】置入 {_players[targetSeat].Name} 的装备区。", targetSeat);
        active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(active with { SelectedCardPayment = active.SelectedCardPayment! with { MovementCommitted = true } });
        if (!TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) &&
            !TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void FreezeProgramSelectedHpPair(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.HpPairSnapshot is not null || active.SelectedTargetSeats is not [var selected] ||
            active.PendingMovementContinuation is not null ||
            active.SelectedCardPayment is not null && active.SelectedCardPaymentResult is not { Completed: true })
            throw new InvalidOperationException("An HP pair must freeze once after its payment children return.");
        var ownerHp = _players[active.OwnerSeat].Hp;
        var targetHp = _players[selected].Hp;
        var awards = active.OwnerSeat != selected && _players[selected].IsAlive && ownerHp != targetHp;
        ReplaceRuntimeTop(active with { HpPairSnapshot = new(active.InstructionIndex, active.OwnerSeat, selected,
            ownerHp, targetHp, _players[active.OwnerSeat].IsAlive && _players[selected].IsAlive,
            awards ? (ownerHp > targetHp ? active.OwnerSeat : selected) : null,
            awards ? (ownerHp < targetHp ? active.OwnerSeat : selected) : null) });
    }

    private void AssertEquipmentPlacementAndHpPair(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.SelectedCardPayment is { } payment)
        {
            if (payment.Operation == SkillProgramEffectOp.PlaceSelectedEquipment)
            {
                var effect = plan.Instructions[payment.InstructionIndex - 1];
                var binding = GetProgramCardSet(frame, effect.SourceBind!);
                if (payment.PlacementSourceLocation is not { } from || from.OwnerSeat != frame.OwnerSeat ||
                    from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                    binding.CardIds.Count != 1 || !binding.CardIds.SequenceEqual(payment.CardIds) ||
                    binding.SourceLocations.Single() != from ||
                    !EquipmentCatalog.IsEquipment(GetAdvancedCard(binding.CardIds[0]).Kind) ||
                    frame.SelectedTargetSeats is not [var seat] || seat != payment.RecipientSeat ||
                    from == CardLocation.Equipment(seat))
                    throw new InvalidOperationException("Equipment placement lost its frozen physical cost origin.");
            }
            else if (payment.PlacementSourceLocation is not null)
                throw new InvalidOperationException("A legacy selected payment cannot carry placement origin.");
        }
        if (frame.HpPairSnapshot is { } pair &&
            (pair.InstructionIndex < 1 || pair.InstructionIndex > frame.InstructionIndex ||
             plan.Instructions[pair.InstructionIndex - 1].Op != SkillProgramEffectOp.FreezeSelectedHpPair ||
             frame.PendingMovementContinuation is not null ||
             frame.SelectedCardPayment is not null && frame.SelectedCardPaymentResult is not { Completed: true } ||
             pair.OwnerSeat != frame.OwnerSeat || frame.SelectedTargetSeats is not [var target] || target != pair.SelectedSeat ||
             (pair.HigherSeat is not null) != (pair.ParticipantsAlive && pair.OwnerSeat != pair.SelectedSeat && pair.OwnerHp != pair.SelectedHp) ||
             (pair.HigherSeat is null) != (pair.LowerSeat is null) ||
             pair.HigherSeat is { } higher && (pair.OwnerSeat == pair.SelectedSeat || pair.OwnerHp == pair.SelectedHp ||
                 higher != (pair.OwnerHp > pair.SelectedHp ? pair.OwnerSeat : pair.SelectedSeat) ||
                 pair.LowerSeat != (pair.OwnerHp < pair.SelectedHp ? pair.OwnerSeat : pair.SelectedSeat))))
            throw new InvalidOperationException("The frozen HP pair lost its producer, participants, or comparison.");
    }

    private sealed partial class ProgramSkillHost
    {
        public SkillProgramStepOutcome PlaceSelectedEquipment(ProgramSkillFrame frame, int targetSeat, string bind) =>
            engine.PlaceProgramSelectedEquipment(frame, targetSeat, bind);
        public void FreezeSelectedHpPair(ProgramSkillFrame frame) => engine.FreezeProgramSelectedHpPair(frame);
    }
}
