namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 掳掠 branch one: the chosen counterpart hands their entire hand to the owner.
    private SkillProgramStepOutcome GiveProgramSelectedTargetHand(ProgramSkillFrame frame, int targetSeat)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var selected] || selected != targetSeat)
            throw new InvalidOperationException("LueLve lost its selected counterpart.");
        var location = CardLocation.Hand(targetSeat);
        var cards = _cardZones.CardsAt(location);
        if (cards.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(active with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCards(cards.ToArray(), location, CardLocation.Hand(frame.OwnerSeat),
            new CardMoveReason($"skill-program.{frame.SkillId}.luelve-gift"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 掳掠 branch two: the flipped counterpart uses one virtual Slash against
    // the skill owner under their own distance and targeting rules. The forced
    // use is not charged to the counterpart's play-phase ledger or alcohol.
    private SkillProgramStepOutcome LuelveProgramCounterSlash(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.SelectedTargetSeats is not [var selected] || selected != selected ||
            selected == frame.OwnerSeat)
            throw new InvalidOperationException("LueLve lost its selected counterpart.");
        if (!_players[selected].IsAlive || !_players[frame.OwnerSeat].IsAlive ||
            ActiveCardAttack is not null || ActiveDuel is not null)
            return SkillProgramStepOutcome.Continue;
        if (IsDirectedCardTargetProhibited(selected, frame.OwnerSeat, CardKind.Slash) ||
            IsSlashProhibited(_players[frame.OwnerSeat]) ||
            GetSeatDistance(selected, frame.OwnerSeat) > GetAttackRange(selected))
            return SkillProgramStepOutcome.Continue;
        var resolutionId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(resolutionId,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, selected, selected, null, selected, null, CardKind.Slash,
            [frame.OwnerSeat], [],
            Array.AsReadOnly(new[] { new CardConversionSource(active.SkillId, GetProgramBindingId(active), selected, active.SkillInstanceId) }),
            effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(resolutionId, selected, 0, CardKind.Slash,
            Array.AsReadOnly(new[] { frame.OwnerSeat }),
            PhysicalCardIds: Array.AsReadOnly(Array.Empty<int>())) { Action = action });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(resolutionId, 0, CardKind.Slash, selected));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(resolutionId, Array.AsReadOnly(new[] { frame.OwnerSeat })));
        var attack = new CardAttackHandle(this, resolutionId, selected, frame.OwnerSeat, card: null,
            damageAmount: 1, playedCardKind: CardKind.Slash,
            programSkillCardUseFrameId: frame.Id);
        ActiveCardAttack = attack;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, selected, frame.OwnerSeat));
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted,
                [frame.OwnerSeat], ProgramCardContinuation.CommittedSlash))
            BeginSlashTargetResolution(attack);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private sealed partial class ProgramSkillHost : ILiangXingProgramHost, ILuelveCounterSlashProgramHost
    {
        public SkillProgramStepOutcome GiveSelectedTargetHand(ProgramSkillFrame frame, int targetSeat) =>
            engine.GiveProgramSelectedTargetHand(frame, targetSeat);

        public SkillProgramStepOutcome SelectedTargetVirtualSlashAgainstOwner(ProgramSkillFrame frame) =>
            engine.LuelveProgramCounterSlash(frame);
    }
}
