namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 断发's phase ledger reads the committed settlement events of the current
    // turn; the skill only ever fires inside the owner's own play phase, so the
    // turn number is exactly the "this phase" scope.
    internal int DuanfaDiscardedThisPhase(int ownerSeat, string skillId) =>
        _events.Select(e => e.Payload).Concat(_pendingEvents).OfType<ProgramDuanfaRecycledEvent>()
            .Where(e => e.OwnerSeat == ownerSeat && e.SkillId == skillId && e.TurnNumber == _turnNumber)
            .Sum(e => e.DiscardedCardIds.Count);

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 90, SkillProgramEffectOp.DuanfaDiscardAndDraw)]
    private bool CanStartDuanfaRecycle(CharacterState owner, string skillId) =>
        DuanfaDiscardedThisPhase(owner.Seat, skillId) < owner.MaxHp &&
        GetHand(owner).Any(card => !IsRedSuit(GetProgramEffectiveSuit(owner, card)));

    // 断发: the pre-selected black hand cards pass the phase cap, are discarded,
    // and the owner draws one-for-one.
    private SkillProgramStepOutcome DuanfaProgramDiscardAndDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        var binding = active.CardSetBindings.SingleOrDefault(item => item.Name == effect.SourceBind!);
        if (binding is null || binding.CardIds.Count == 0)
        {
            CancelProgramBindingAndCleanup(active, "断发的选牌绑定未生成，技能结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (binding.CardIds.Any(cardId => _cardZones.GetLocation(cardId) != CardLocation.Hand(owner.Seat)))
        {
            CancelProgramBindingAndCleanup(active, "断发所选的牌已离开手牌区，技能结算取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var allowance = owner.MaxHp - DuanfaDiscardedThisPhase(owner.Seat, active.SkillId);
        if (allowance <= 0 || binding.CardIds.Count > allowance)
        {
            CancelProgramBindingAndCleanup(active,
                "断发本阶段至多以此法弃置体力上限张牌，本次结算取消，所选牌未移动。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var cards = binding.CardIds
            .Select(cardId => _cardZones.CardsAt(CardLocation.Hand(owner.Seat)).Single(card => card.Id == cardId))
            .ToArray();
        MoveLogicalDiscardCards(active.OwnerSeat, active.OwnerSeat, cards, CardLocation.Hand(owner.Seat),
            new($"skill-program.{active.SkillId}.duanfa-discard"));
        DrawCards(owner, cards.Length, true, new($"skill-program.{active.SkillId}.duanfa-draw"));
        AdvanceEventRulesAndQueueFact(new ProgramDuanfaRecycledEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber,
            Array.AsReadOnly(cards.Select(card => card.Id).ToArray()), cards.Length));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
}
