namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidPairObtain(ProgramSkillFrame f)
    {
        if (f.PairObtain is not { } d || d.InstructionIndex != 0 || f.InstructionIndex is < 1 or > 4 || f.TriggerId is not null || f.WindowContext is not null ||
            d.Source != PairBenefitSource(f) || d.GameplayHash != f.GameplayHash || d.ActualTurnNumber != _turnNumber || d.ActualTurnOwnerSeat != _currentSeat ||
            d.ActualTurnOwnerSeat != f.OwnerSeat || d.PhaseInstanceId != _cardUseDebitPhaseInstanceId || d.Cursor is < 0 or > 2 ||
            d.FirstSeat == d.SecondSeat || !IsValidPlayerSeat(d.FirstSeat) || !IsValidPlayerSeat(d.SecondSeat) ||
            !f.SelectedTargetSeats.SequenceEqual([d.FirstSeat, d.SecondSeat]) || d.Cursor == 2 && d.AwaitingMovement ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not
                [{ Op: SkillProgramEffectOp.ObtainOneFromEachSelectedTarget }, { Op: SkillProgramEffectOp.SelectOwnedCards },
                 { Op: SkillProgramEffectOp.RevealBoundCards }, { Op: SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget }]) return false;
        if (CompleteProgramEventHistory().OfType<PairObtainStartedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Source == d.Source &&
            e.GameplayHash == d.GameplayHash && e.ActualTurnNumber == d.ActualTurnNumber && e.ActualTurnOwnerSeat == d.ActualTurnOwnerSeat &&
            e.PhaseInstanceId == d.PhaseInstanceId && e.FirstSeat == d.FirstSeat && e.SecondSeat == d.SecondSeat) != 1) return false;
        bool Paid(ProgramPairObtainPayment? p, int cursor, int target)
        {
            if (p is null) return d.Cursor <= cursor && !(d.Cursor == cursor && d.AwaitingMovement);
            if (p.Cursor != cursor || p.RecipientSeat != f.OwnerSeat || p.From.OwnerSeat != target ||
                p.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment) || p.SequenceBefore < 0 || p.SequenceAfter < p.SequenceBefore ||
                p.SameHand != (p.From == CardLocation.Hand(f.OwnerSeat)) || p.SameHand && (p.SequenceAfter != p.SequenceBefore || p.Delivered) ||
                !p.SameHand && p.SequenceAfter <= p.SequenceBefore) return false;
            var reason = PairBenefitMoveReason(f, false);
            if (p.SameHand ? _cardMovements.Any(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.CardId == p.CardId)
                : _cardMovements.Count(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.CardId == p.CardId &&
                    m.From == p.From && (m.To == CardLocation.Hand(f.OwnerSeat) || m.To == CardLocation.OutsideGame && p.From.Zone == CardZoneKind.Equipment) && m.Reason.Value == reason) != 1) return false;
            if (!p.SameHand && p.Delivered != _cardMovements.Any(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter && m.CardId == p.CardId &&
                m.From == p.From && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == reason)) return false;
            return CompleteProgramEventHistory().OfType<PairObtainStepCommittedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Cursor == p.Cursor &&
                e.CardOwnerSeat == target && e.RecipientSeat == p.RecipientSeat && e.SequenceBefore == p.SequenceBefore && e.SequenceAfter == p.SequenceAfter &&
                e.SameHand == p.SameHand && e.Delivered == p.Delivered) == 1;
        }
        return Paid(d.FirstPayment, 0, d.FirstSeat) && Paid(d.SecondPayment, 1, d.SecondSeat) &&
            (d.FirstPayment is null || d.SecondPayment is null || d.SecondPayment.SequenceBefore >= d.FirstPayment.SequenceAfter);
    }
    private bool ValidShownPairGift(ProgramSkillFrame f)
    {
        if (!ValidPairObtain(f) || f.ShownPairGift is not { } r || r.InstructionIndex != 3 || f.InstructionIndex != 4 ||
            r.FirstSeat != f.PairObtain!.FirstSeat || r.SecondSeat != f.PairObtain.SecondSeat || r.FrozenFirstHandCount < 0 || r.FrozenSecondHandCount < 0 ||
            !Enum.IsDefined(r.Stage) || !Enum.IsDefined(r.FrozenSuit) || r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore ||
            f.CardSetBindings.SingleOrDefault(b => b.Name == r.SourceBind) is not { Visibility: SkillProgramCardSetVisibility.Public } binding ||
            !binding.CardIds.SequenceEqual([r.CardId]) || !binding.SourceLocations.SequenceEqual([CardLocation.Hand(f.OwnerSeat)]) || binding.FrozenRevealedSuit != r.FrozenSuit) return false;
        if (r.Stage == ProgramShownPairGiftStage.ChoosingRecipient)
            return r.RecipientSeat is null && !r.Delivered && !r.SameHand && r.SequenceAfter == r.SequenceBefore;
        if (r.RecipientSeat is not { } recipient || recipient != r.FirstSeat && recipient != r.SecondSeat || r.SameHand != (recipient == f.OwnerSeat) ||
            (recipient == r.FirstSeat ? r.FrozenFirstHandCount : r.FrozenSecondHandCount) != Math.Min(r.FrozenFirstHandCount, r.FrozenSecondHandCount) ||
            (r.SameHand ? r.SequenceAfter != r.SequenceBefore || r.Delivered : r.SequenceAfter <= r.SequenceBefore)) return false;
        if (!r.SameHand && _cardMovements.Count(m => m.Sequence > r.SequenceBefore && m.Sequence <= r.SequenceAfter && m.CardId == r.CardId &&
            m.From == CardLocation.Hand(f.OwnerSeat) && m.To == CardLocation.Hand(recipient) && m.Reason.Value == PairBenefitMoveReason(f, true)) != 1) return false;
        if (CompleteProgramEventHistory().OfType<ShownPairGiftCommittedEvent>().Count(e => e.ProgramFrameId == f.Id && e.SourceBind == r.SourceBind &&
            e.ShownCardId == r.CardId && e.FrozenSuit == r.FrozenSuit && e.RecipientSeat == recipient && e.FrozenFirstHandCount == r.FrozenFirstHandCount &&
            e.FrozenSecondHandCount == r.FrozenSecondHandCount && e.SequenceBefore == r.SequenceBefore && e.SequenceAfter == r.SequenceAfter &&
            e.SameHand == r.SameHand && e.Delivered == r.Delivered) != 1) return false;
        if (r.Stage == ProgramShownPairGiftStage.AwaitingGift) return r.DrawSequenceBefore == 0 && r.DrawSequenceAfter == 0 && r.ActualDrawCount == 0;
        var draws = CompleteProgramEventHistory().OfType<ShownPairGiftRewardIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.FrozenSuit == Suit.Spade) return r.Stage == ProgramShownPairGiftStage.Complete && draws.Length == 0;
        return r.DrawSequenceBefore >= r.SequenceAfter && r.DrawSequenceAfter >= r.DrawSequenceBefore && r.ActualDrawCount is >= 0 and <= 1 && draws is [var draw] &&
            draw.OwnerSeat == f.OwnerSeat && draw.RequestedCount == 1 && draw.ActualCount == r.ActualDrawCount && draw.SequenceBefore == r.DrawSequenceBefore && draw.SequenceAfter == r.DrawSequenceAfter &&
            _cardMovements.Count(m => m.Sequence > r.DrawSequenceBefore && m.Sequence <= r.DrawSequenceAfter && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == $"{PairBenefitMoveReason(f, true)}.reward") == r.ActualDrawCount;
    }
    private void AssertPairBenefitState(ProgramSkillFrame f)
    {
        if (f.PairObtain is not null && !ValidPairObtain(f) || f.ShownPairGift is not null && !ValidShownPairGift(f) ||
            f.FixedRecipient is not null && !ValidFixedRecipientReceipt(f) ||
            (f.PairObtain is not null || f.ShownPairGift is not null) && f.PendingMovementContinuation is { } pending &&
                (pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null))
            throw new InvalidOperationException("A pair/fixed-recipient receipt differs from its exact original producer, binding or real ledger.");
    }
    private bool IsPairBenefitMovement(ProgramSkillFrame f, SkillProgramEffect? paid, ProgramMovementContinuation movement) =>
        movement.SubjectSeat == f.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null &&
        (paid?.Op == SkillProgramEffectOp.ObtainOneFromEachSelectedTarget && f.PairObtain is { AwaitingMovement: true } && ValidPairObtain(f) ||
         paid?.Op == SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget &&
            f.ShownPairGift?.Stage is ProgramShownPairGiftStage.AwaitingGift or ProgramShownPairGiftStage.AwaitingReward && ValidShownPairGift(f));
}
