namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HalfHandPhaseFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.PendingMovementContinuation is not { SubjectSeat: var subject, BeforeCount: 0, CoverageResultBind: null } || subject != f.OwnerSeat) return false;
        long before, after; string reason; int source, target; IReadOnlyList<int> ids;
        if (f.HalfHandDraw is { } drawn && ValidHalfHandDraw(f))
        { before = drawn.SequenceBefore; after = drawn.SequenceAfter; source = -1; target = f.OwnerSeat; ids = []; reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.DrawExtraAndArmHalfHandSupport}"; }
        else if (f.HalfHandGift is { } gift && ValidHalfHandGift(f))
        { before = gift.SequenceBefore; after = gift.SequenceAfter; source = f.OwnerSeat; target = gift.RecipientSeat; ids = gift.CardIds; reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveHalfHandAndIssueTargetSupport}"; }
        else if (f.PhaseHandExchange is { Issued: true } exchange && ValidPhaseHandExchange(f))
        { before = exchange.SequenceBefore; after = exchange.SequenceAfter; source = -2; target = -2; ids = Array.AsReadOnly(exchange.FirstCardIds.Concat(exchange.SecondCardIds).ToArray()); reason = $"skill-program.{f.SkillId}.exchange"; }
        else if (f.HalfHandSupport is { Paid: true, CardId: { } cardId } aid &&
            _resolutionStack.FirstOrDefault(parent => parent.Id == f.WindowContext?.ParentFrameId) is ActualUseTargetWindowFrame supportWindow &&
            HalfHandSupportPaymentMatches(f, supportWindow, true))
        { before = aid.SequenceBefore; after = aid.SequenceAfter; source = aid.RecipientSeat; target = f.OwnerSeat; ids = [cardId]; reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.OfferHalfHandRecipientSupport}"; }
        else if (f.PhaseHandDebtPayment is { } paid && ValidPhaseHandDebtPayment(f) && f.InstructionIndex is 2 or 3 &&
            f.CardSetBindings.SingleOrDefault(b => b.Name == paid.ResultBind) is { } binding && binding.CardIds.Count == paid.RequiredPaymentCount &&
            binding.SourceLocations.Count == binding.CardIds.Count && binding.SourceLocations.All(l => l.OwnerSeat == f.OwnerSeat && l.Zone is CardZoneKind.Hand or CardZoneKind.Equipment))
        {
            before = paid.SequenceBefore; after = HalfHandDebtSequence; source = f.OwnerSeat; target = -3; ids = binding.CardIds; reason = $"skill-program.{f.SkillId}.{SkillProgramEffectOp.MoveBoundCards}";
            if (ids.Count == 0 || ids.Where((id, n) => _cardMovements.Count(m => m.Sequence > before && m.Sequence <= after && m.CardId == id &&
                m.From == binding.SourceLocations[n] && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && binding.SourceLocations[n].Zone == CardZoneKind.Equipment) &&
                m.Reason.Value == reason) != 1).Any()) return false;
        }
        else return false;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == f.Id && (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId == f.Id) &&
                moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > before && m.Sequence <= after && m.Reason.Value == reason &&
                    (ids.Count == 0 || ids.Contains(m.CardId)) && (source != -1 || m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(target)));
        if (target != -3) return false;
        var lions = _cardMovements.Where(m => m.Sequence > before && m.Sequence <= after && ids.Contains(m.CardId) &&
            m.From == CardLocation.Equipment(f.OwnerSeat) && m.CardKind == CardKind.SilverLion && m.Reason.Value == reason).ToArray();
        if (child is HpChangedTriggerWindowFrame hp)
            return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1 && lions.Length == 1;
        return child is RecoveryReplacementFrame replacement && RecoveryReplacementFrameRidesOn(replacement, f) &&
            replacement.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && replacement.Attempt.SourceSeat == f.OwnerSeat && replacement.Attempt.TargetSeat == f.OwnerSeat &&
            replacement.Attempt.Amount == 1 && replacement.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            replacement.Attempt.Completion.MoveReason?.Value == reason && lions.Length == 1;
    }
    private ProgramSkillFrame? HalfHandPhaseDebtObserverRoot()
    {
        for (var rootIndex = 0; rootIndex + 1 < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramSkillFrame f || !HalfHandPhaseFirstChild(f, _resolutionStack[rootIndex + 1])) continue;
            var aligned = true;
            for (var index = rootIndex + 1; index < _resolutionStack.Count; index++)
            {
                if (!HalfHandPaidDamageObserverEdge(index)) { aligned = false; break; }
                if (_resolutionStack[index] is DyingFrame d && (IsPaidHandRepaymentProgramAlcoholRide(index, d) ||
                    IsPaidHandRepaymentRescueRide(index, d) || PolicyCounterspellVirtualAlcoholRide(index, d) || PaidObserverDamageVirtualAlcoholRide(index, d))) break;
            }
            if (aligned) return f;
        }
        return null;
    }
    private bool HalfHandSupportObserverRootMatches(ProgramSkillFrame f, ActualUseTargetWindowFrame parent)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        return index >= 0 && index + 1 < _resolutionStack.Count && HalfHandSupportPaymentMatches(f, parent, true) &&
            HalfHandPhaseFirstChild(f, _resolutionStack[index + 1]);
    }
    private bool IsHalfHandPhaseDebtProgramDying() => ActiveDying is { } dying &&
        (HalfHandPhaseDebtObserverRoot() ?? _resolutionStack.OfType<ActualUseTargetWindowFrame>()
            .Select(w => ActualUseTargetObserverRoot(w.ParentFrameId)).LastOrDefault(f => f?.HalfHandSupport is { Paid: true })) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.FrameId) > _resolutionStack.FindIndex(f => f.Id == root.Id);
}
