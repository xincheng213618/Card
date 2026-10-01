namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ResolveEnteringChain(CharacterState player, bool requested) =>
        requested && !player.IsChained &&
        HasCardPolicy(player, SkillProgramCardPolicyKind.PreventEnteringChain) ? false : requested;

    private ProgramLegalityParticipant CreateProgramLegalityParticipant(int seat) =>
        new(seat, GetHand(_players[seat]).Count,
            HasCardPolicy(_players[seat], SkillProgramCardPolicyKind.ProhibitPindianTarget));

    private bool CanBePindianTarget(int sourceSeat, int targetSeat) =>
        ProgramOperationLegalityPolicy.HandContest.CanSelectTarget(
            new(sourceSeat, GetHand(_players[sourceSeat]).Count), CreateProgramLegalityParticipant(targetSeat));

    private bool CanPayEquipmentSlotGroup(CharacterState owner, SkillProgramActivation activation) =>
        ProgramInstructionResolver.Default.Features(activation).ForOperation(SkillProgramEffectOp.AbolishEquipmentSlotGroup)
            .All(e => e.EquipmentSlots.Any(slot => owner.EquipmentSlotCapacity(slot) > 0));

    private bool CanSelectProgramPindianOpponent(ProgramSkillFrame frame, int targetSeat)
    {
        if (CanBePindianTarget(frame.OwnerSeat, targetSeat)) return true;
        var instructions = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!).Instructions;
        // Only the immediately following unconditional owner contest consumes this selection.
        // Conditional or later branches retain their independent target domain.
        if (frame.InstructionIndex < instructions.Count &&
            instructions[frame.InstructionIndex] is
            {
                Op: SkillProgramEffectOp.StartPindian,
                Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always,
                OpponentReference.Kind: ProgramParticipantRef.SelectedTarget
            })
            return false;
        return true;
    }
    private sealed partial class ProgramSkillHost : IEquipmentSlotGroupProgramHost
    {
        public void AbolishEquipmentSlotGroup(ProgramSkillFrame frame, IReadOnlyList<EquipmentSlot> slots) =>
            engine.AbolishProgramEquipmentSlotGroup(frame, slots);

        public void RecastSelectedEquipment(ProgramSkillFrame frame) => engine.RecastProgramSelectedEquipment(frame);

        public void ReplaceSkillsOnPreparation(ProgramSkillFrame frame, IReadOnlyList<string> lost, string acquired) =>
            engine.ReplaceProgramPreparationSkills(frame, lost, acquired);
    }

    private void AbolishProgramEquipmentSlotGroup(ProgramSkillFrame frame, IReadOnlyList<EquipmentSlot> slots)
    {
        frame = GetActiveProgramFrame(frame.Id);
        var owner = _players[frame.OwnerSeat];
        if (frame.TriggerId is not null || _phase != TurnPhase.Play || _currentSeat != owner.Seat ||
            !owner.IsAlive || slots.Count == 0 || !slots.Any(slot => owner.EquipmentSlotCapacity(slot) > 0))
            throw new InvalidOperationException("Slot payment lost its exact own Play activation or remaining cost.");
        foreach (var slot in slots.Where(slot => owner.EquipmentSlotCapacity(slot) > 0))
            SetEquipmentSlotCapacity(owner, slot, 0);
    }

    private void RecastProgramSelectedEquipment(ProgramSkillFrame frame)
    {
        frame = GetActiveProgramFrame(frame.Id);
        var owner = _players[frame.OwnerSeat];
        if (frame.TriggerId is not null || frame.SelectedCardIds is not [var id] ||
            _phase != TurnPhase.Play || _currentSeat != owner.Seat || !owner.IsAlive)
            throw new InvalidOperationException("Equipment recast lost its actual owner activation.");
        var location = _cardZones.GetLocation(id);
        if (location.OwnerSeat != owner.Seat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("Equipment recast requires a real owned hand/equipment entity.");
        var card = _cardZones.CardsAt(location).Single(c => c.Id == id);
        if (!EquipmentCatalog.IsEquipment(card.Kind))
            throw new InvalidOperationException("Recast entity is not equipment.");
        MoveCard(card, location, CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        var drawn = DrawCards(owner, 1, true, CardMoveReasons.RecastDraw);
        AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, card.Id, card.Kind, drawn.Count));
    }

    private void ReplaceProgramPreparationSkills(ProgramSkillFrame frame, IReadOnlyList<string> lost, string acquired)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, OwnerSeat: var seat } ||
            seat != frame.OwnerSeat || _currentSeat != frame.OwnerSeat || !_players[seat].IsAlive)
            throw new InvalidOperationException("Preparation replacement lost its own actual turn boundary.");
        var owner = _players[seat];
        GrantProgramSkills(frame, [acquired]);
        foreach (var grant in owner.SkillGrants.Grants.Where(g => lost.Contains(g.SkillId)).ToArray())
            owner.SkillGrants.RemoveGrant(grant.GrantId);
        AdvanceEventRulesAndQueueFact(new ProgramOwnerSkillsReplacedEvent(
            frame.Id, frame.SkillId, owner.Seat, Array.AsReadOnly(lost.ToArray()), acquired));
    }
}
