namespace CardGame.Core;
public sealed partial class GameEngine
{
    // Called by the eventual exact participant Give producer only inside its
    // successful native physical-record callback. Admission stays in that port.
    private void RecordMouChenLinGift(ProgramSkillFrame f, int giver, int cardId, long before, IReadOnlyList<CardMovementRecord> movements)
    {
        if (f.MouChenLinInvitation is not { Closed: false } invitation || f.SkillId != MouChenLinRuntime.Yaozuo || invitation.Source != new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) || invitation.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || invitation.Phase.TurnNumber != _turnNumber || !IsRecordedActualDiscardRecoveryPhase(invitation.Phase) || invitation.Phase.ActorSeat != f.OwnerSeat || !invitation.Eligible.Contains(giver) || invitation.Answered.Contains(giver) || movements is not [var m] || !_cardMovements.Contains(m) || m.CardId != cardId || m.Sequence <= before || m.From.OwnerSeat != giver || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || m.To != CardLocation.Hand(f.OwnerSeat)) throw new InvalidOperationException("Yaozuo requires its own accepted participant's successful actual HE-to-Hand Give invoice.");
        var gift = new MouChenLinGift(giver, cardId, m.From, m.Sequence);
        ReplaceRuntimeFrame(f.Id, f with { MouChenLinInvitation = invitation with { FirstGiver = invitation.FirstGiver ?? giver, Answered = invitation.Answered.Append(giver).ToArray(), Gifts = invitation.Gifts.Append(gift).ToArray() } });
        AdvanceEventRulesAndQueueFact(new MouChenLinGiftPaidEvent(f.Id, invitation.Source, invitation.GameplayHash, _turnNumber, giver, cardId, m.From, m.Sequence));
        AdvanceEventRulesAndQueueFact(new MouChenLinInvitationAnswerEvent(f.Id, giver, true));
        if (invitation.FirstGiver is null) AdvanceEventRulesAndQueueFact(new MouChenLinFirstGiftEvent(f.Id, giver, cardId, m.Sequence));
    }
    private bool IsMouChenLinHandLimitExempt(CharacterState owner, int cardId) => HasMouChenLin && _cardZones.GetLocation(cardId) == CardLocation.Hand(owner.Seat) && ProgramEventHistory<MouChenLinGiftPaidEvent>().Any(e => e.Source.OwnerSeat == owner.Seat && e.CardId == cardId && e.Source.SkillId == MouChenLinRuntime.Yaozuo && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash && _cardMovements.Any(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == e.From && m.From.OwnerSeat == e.Giver && m.To == CardLocation.Hand(owner.Seat)));
    private void IssueMouChenLinNonHandDonorTickets(ProgramSkillFrame f)
    {
        if (f.MouChenLinInvitation is not { Closed: true } r || r.Phase.TurnNumber != _turnNumber || r.Answered.Count != r.Eligible.Count || r.Eligible.Except(r.Answered).Any()) throw new InvalidOperationException("Yaozuo's next-damage tickets require the actual closed invitation and all real participant returns.");
        foreach (var target in r.Eligible.Where(s => !r.Gifts.Any(g => g.Giver == s && g.From == CardLocation.Hand(s))))
            if (!ProgramEventHistory<MouChenLinDamageTicketEvent>().Any(e => e.FrameId == f.Id && e.Target == target)) AdvanceEventRulesAndQueueFact(new MouChenLinDamageTicketEvent(f.Id, r.Source, r.GameplayHash, _turnNumber, target));
    }
}
