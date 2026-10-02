namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PaidPhaseGiftUsage = "paid-phase-gift-count";

    private void CapturePaidPhaseGift(long frameId, SkillProgramActivation activation, bool toDiscard)
    {
        if (toDiscard || !ProgramInstructionResolver.Default.Features(activation).HasOperation(SkillProgramEffectOp.AccumulatePaidPhaseGift)) return;
        var frame = GetActiveProgramFrame(frameId);
        var payment = frame.SelectedCardPayment ?? throw new InvalidOperationException("A phase gift requires an owning physical payment.");
        if (!payment.MovementCommitted || payment.Operation != SkillProgramEffectOp.GiveSelected || frame.PhaseGiftReceipt is not null)
            throw new InvalidOperationException("A phase gift cannot be counted before payment or twice.");
        var before = _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, PaidPhaseGiftUsage, SkillUsageScope.Phase);
        foreach (var cardId in payment.CardIds)
        {
            if (_cardZones.GetLocation(cardId) != CardLocation.Hand(payment.RecipientSeat))
                throw new InvalidOperationException("The committed gift entity lost its recipient before children.");
            if (!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, PaidPhaseGiftUsage, SkillUsageScope.Phase, int.MaxValue))
                throw new InvalidOperationException("The actual phase gift count could not advance.");
        }
        ReplaceRuntimeTop(frame with { PhaseGiftReceipt = new(payment.InstructionIndex, _cardUseDebitPhaseInstanceId, before, payment.CardIds.Count) });
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(frame.OwnerSeat, frame.SkillId, PaidPhaseGiftUsage, SkillUsageScope.Phase, payment.CardIds.Count));
    }

    private void AccumulatePaidPhaseGift(ProgramSkillFrame frame, string resultBind, int threshold)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var receipt = active.PhaseGiftReceipt ?? throw new InvalidOperationException("An actual phase gift receipt is required.");
        if (receipt.PhaseInstanceId != _cardUseDebitPhaseInstanceId || active.SelectedCardPaymentResult is not { Completed: true } result || result.InstructionIndex != receipt.InstructionIndex || active.ChoiceBindings.Any(b => b.Name == resultBind))
            throw new InvalidOperationException("A phase gift lost its completed exact payment or result binding.");
        ReplaceRuntimeTop(active with { ChoiceBindings = Array.AsReadOnly(active.ChoiceBindings.Append(new ProgramChoiceResultBinding(resultBind,
            receipt.PreviousCount < threshold && receipt.PreviousCount + receipt.ActualCount >= threshold ? "crossed" : "not-crossed", active.OwnerSeat)).ToArray()) });
    }

    private sealed partial class ProgramSkillHost : IPhaseGiftBasicProgramHost
    {
        public void AccumulatePaidPhaseGift(ProgramSkillFrame frame, string resultBind, int threshold) => engine.AccumulatePaidPhaseGift(frame, resultBind, threshold);
        public SkillProgramStepOutcome OfferVirtualBasicCard(ProgramSkillFrame frame) => engine.OfferVirtualBasicCard(frame);
    }
}
