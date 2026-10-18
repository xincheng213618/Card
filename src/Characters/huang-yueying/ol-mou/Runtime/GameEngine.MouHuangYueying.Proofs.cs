namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouBingcaiStarted(ProgramSkillFrame f) => f.MouBingcai is { } r && f.SkillId == MouBingcaiSkill &&
        f.TriggerId == "first-basic-recast" && f.ActivationId == f.TriggerId && f.InstructionIndex == 1 &&
        f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && r.Source == MouHuangYueyingSource(f) && r.GameplayHash == f.GameplayHash &&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash && f.WindowContext is { } context &&
        TryGetMouBingcaiFirstBasic(context, out var use, out var window) && r.WindowId == window.Id && r.UseId == use.Id &&
        r.ActionId == window.Action.ActionId && r.BasicKind == use.CardKind &&
        ProgramEventHistory<MouBingcaiStartedEvent>().Count(e => e == new MouBingcaiStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.UseId, r.ActionId, r.BasicKind)) == 1 &&
        ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
            e.BindingId == f.TriggerId && e.SkillInstanceId == f.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) == 1;

    // Pure proof of this producer's original payment; never calls extra-round validation.
    private bool IsPaidMouBingcai(ProgramSkillFrame f)
    {
        if (!ValidMouBingcaiStarted(f) || f.MouBingcai is not { Paid: true, MaterialId: { } id, PrintedKind: { } printed,
            EffectiveKind: { } effective, From: { } from } r || from.OwnerSeat != f.OwnerSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || r.RecastInvocationId <= 0 || r.BatchId <= 0 ||
            r.MovementSequence <= r.Before || r.MovementSequence > r.After || r.After > _movementSequence ||
            r.MatchesDamage != (IsMouHuangYueyingDamageCardKind(r.BasicKind) == IsMouHuangYueyingDamageCardKind(effective)) ||
            r.IsTrick != (CardUseCategoryCatalog.Get(effective) is CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick) ||
            ProgramEventHistory<MouBingcaiRecastPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
            paid != new MouBingcaiRecastPaidEvent(f.Id, r.RecastInvocationId, r.BatchId, f.OwnerSeat, id, printed, effective, from,
                r.MovementSequence, r.Before, r.After, r.MatchesDamage, r.IsTrick)) return false;
        var payment = _cardMovements.Where(m => m.Sequence == r.MovementSequence && m.CardId == id && m.CardKind == printed &&
            m.From == from && m.Reason == CardMoveReasons.RecastDiscard).ToArray();
        if (payment is not [var m] || m.To != CardLocation.DiscardPile && !(m.To == CardLocation.OutsideGame &&
            (GetAdvancedCard(id).IsGeneralWeapon && from.Zone == CardZoneKind.Equipment || IsGuanduThunderChariotDeparture(GetAdvancedCard(id), from, m.To)))) return false;
        var invoices = ProgramEventHistory<NativeRecastInvocationRecordedEvent>().Where(e => e.InvocationId == r.RecastInvocationId).ToArray();
        if (f.NativeRecastInvocation is { } draft)
            return invoices.Length == 0 && draft.InvocationId == r.RecastInvocationId && draft.ParentFrameId == f.Id && draft.InstructionIndex == f.InstructionIndex &&
                draft.InitiatorSeat == f.OwnerSeat && draft.ExecutorSeat == f.OwnerSeat && draft.Materials is [{ } cost] &&
                cost.CardId == id && cost.PrintedKind == printed && cost.From == from && draft.Reason == CardMoveReasons.RecastDiscard && draft.SequenceBefore == r.Before;
        return invoices is [var invoice] && invoice.ParentFrameId == f.Id && invoice.InstructionIndex == f.InstructionIndex &&
            invoice.InitiatorSeat == f.OwnerSeat && invoice.ExecutorSeat == f.OwnerSeat && invoice.PrimaryCount == 1 && invoice.ActualRecastCount == 1 &&
            invoice.SequenceBefore == r.Before && invoice.SequenceAfter >= r.After && invoice.ActualDrawCount == r.ActualDrawIds.Count &&
            r.Stage is MouBingcaiStage.DrawChildren or MouBingcaiStage.AddNameChoice;
    }
    private bool MouBingcaiPrimaryMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.MouBingcai is not { } r || !_cardMovements.Contains(m)) return false;
        if (r.Stage == MouBingcaiStage.CostChildren) return m.CardId == r.MaterialId && m.CardKind == r.PrintedKind && m.From == r.From &&
            m.Sequence == r.MovementSequence && m.Reason == CardMoveReasons.RecastDiscard;
        return r.Stage == MouBingcaiStage.DrawChildren && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter &&
            (r.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == CardMoveReasons.RecastDraw ||
             r.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool MouBingcaiOxSpill(ProgramSkillFrame f, CardMovementBatchContext b) => f.MouBingcai is { Stage: MouBingcaiStage.CostChildren,
        PrintedKind: CardKind.WoodenOx, From: { Zone: CardZoneKind.Equipment } } r && b.ParentBatchId == r.BatchId && b.Movements.Count > 0 &&
        b.Movements.All(m => m.From == CardLocation.WoodenOxGrain(f.OwnerSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && _cardMovements.Contains(m));
    private bool IsMouHuangYueyingMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.MouBingcai is { Stage: MouBingcaiStage.CostChildren or MouBingcaiStage.DrawChildren } && effect?.Op == SkillProgramEffectOp.ResolveMouHuangYueyingBingcai &&
        IsPaidMouBingcai(f) && pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null ||
        IsMouLixianMovement(f, effect, pending);
    private bool IsMouHuangYueyingRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.MouBingcai is { Stage: MouBingcaiStage.CostChildren, PrintedKind: CardKind.SilverLion, From: { Zone: CardZoneKind.Equipment } } &&
        IsPaidMouBingcai(f) && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == CardMoveReasons.RecastDiscard &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1 || IsMouLixianRecoveryParent(f, recovery);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28961)]
    private void AssertMouBingcai(ProgramSkillFrame f)
    {
        if (f.MouBingcai is not { } r) return;
        if (!Enum.IsDefined(r.Stage) || !ValidMouBingcaiStarted(f) || r.Paid && !IsPaidMouBingcai(f) ||
            r.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } || r.ReplacementGainedIds is not System.Collections.IList { IsReadOnly: true } ||
            !r.Paid && (r.Stage != MouBingcaiStage.MaterialChoice || r.MaterialId is not null || f.NativeRecastInvocation is not null || f.PendingMovementContinuation is not null))
            throw new InvalidOperationException("Bingcai changed its original first Basic source, readonly native outcome or once-paid recast.");
        if (r.Stage is MouBingcaiStage.DrawChildren or MouBingcaiStage.AddNameChoice)
        {
            var returns = ProgramEventHistory<MouBingcaiDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (returns is not [var returned] || returned != new MouBingcaiDrawReturnedEvent(f.Id, r.DrawAttemptFrameId, r.DrawBefore, r.DrawAfter, r.ActualDrawIds.Count, r.ReplacementGainedIds.Count) ||
                r.ActualDrawIds.Distinct().Count() != r.ActualDrawIds.Count || r.ReplacementGainedIds.Distinct().Count() != r.ReplacementGainedIds.Count ||
                r.ActualDrawIds.Any(id => !_cardMovements.Any(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) &&
                    m.Reason == CardMoveReasons.RecastDraw && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter)) ||
                r.ReplacementGainedIds.Any(id => !_cardMovements.Any(m => m.CardId == id && m.To == CardLocation.Hand(f.OwnerSeat) && m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter)))
                throw new InvalidOperationException("Bingcai lost its exact typed real Draw1/replacement return.");
        }
    }
}
