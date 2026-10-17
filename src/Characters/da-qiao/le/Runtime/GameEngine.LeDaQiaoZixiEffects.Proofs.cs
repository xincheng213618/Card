namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidZixiEffectsStarted(ProgramSkillFrame f)
    {
        if (f.ZixiEffects is not { } r || f.SkillId != "le:zixi" || f.TriggerId != "unique-target-judgment-effects" ||
            f.InstructionIndex != 1 || r.Source != ZixiEffectsSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects ||
            f.WindowContext is not { } context || context.ParentFrameId != r.WindowId ||
            !TryGetZixiEffectsUse(f.OwnerSeat, context, out var use, out var window, out var target) ||
            use.Id != r.UseId || use.Action!.ActionId != r.ActionId || use.CardKind != r.CardKind || target != r.TargetSeat ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
                f.GameplayHash, GetProgramTrigger(f).Priority), context, window)) return false;
        return ProgramEventHistory<ZixiEffectsStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var started] &&
            started == new ZixiEffectsStartedEvent(f.Id, r.Source, r.GameplayHash, r.UseId, r.ActionId, r.WindowId,
                r.TargetSeat, r.JudgmentCount, r.CardKind, r.SequenceBefore) &&
            ProgramEventHistory<ZixiEffectsStartedEvent>().Count(e => e.UseId == r.UseId && e.Source.OwnerSeat == f.OwnerSeat) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28720)]
    private void AssertZixiEffects(ProgramSkillFrame f)
    {
        if (f.ZixiEffects is not { } r) return;
        if (!ValidZixiEffectsStarted(f) || !Enum.IsDefined(r.Stage) || r.JudgmentCount is < 1 or > 3 ||
            r.Before < r.SequenceBefore || r.After < r.Before || r.After > _movementSequence ||
            r.Entities is not System.Collections.IList { IsReadOnly: true } ||
            r.Entities.Select(c => c.CardId).Distinct().Count() != r.Entities.Count ||
            r.Entities.Any(c => c.From.OwnerSeat != r.TargetSeat || c.From.Zone is not (CardZoneKind.Judgment or CardZoneKind.Equipment)))
            throw new InvalidOperationException("Zixi lost its original finalized singleton Use, signed source or frozen judgment count.");
        if (r.JudgmentCount == 2)
        {
            if (r.Entities.Count != 0 || r.ActualCount != -1 || r.BatchId != 0 || r.Stage is not (ZixiEffectsStage.Drawing or ZixiEffectsStage.DrawChildren))
                throw new InvalidOperationException("Zixi's Draw2 cannot enter another judgment-count branch.");
            if (r.DrawOutcome is { } draw)
            {
                if (r.Stage != ZixiEffectsStage.DrawChildren || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != 2 ||
                    draw.SequenceBefore != r.Before || draw.SequenceAfter != r.After || draw.ActualDrawIds.Count > 2 ||
                    draw.ActualDrawIds is not System.Collections.IList { IsReadOnly: true } ||
                    draw.ReplacementGainedIds is not System.Collections.IList { IsReadOnly: true } ||
                    draw.ActualDrawIds.Distinct().Count() != draw.ActualDrawIds.Count ||
                    draw.ReplacementGainedIds.Distinct().Count() != draw.ReplacementGainedIds.Count ||
                    draw.ActualDrawIds.Intersect(draw.ReplacementGainedIds).Any() || draw.Replacement is not null && draw.ActualDrawIds.Count != 0 ||
                    draw.ActualDrawIds.Any(id => _cardMovements.Count(m => m.CardId == id && m.Sequence > r.Before && m.Sequence <= r.After &&
                        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZixiEffectsDrawReason) != 1) ||
                    _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile &&
                        m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZixiEffectsDrawReason) != draw.ActualDrawIds.Count ||
                    ProgramEventHistory<ZixiEffectsDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var returned] ||
                    returned != new ZixiEffectsDrawReturnedEvent(f.Id, f.OwnerSeat, 2, draw.ActualDrawIds.Count,
                        draw.ReplacementGainedIds.Count, r.Before, r.After, draw.AttemptFrameId))
                    throw new InvalidOperationException("Zixi lost its exact native Draw2 return or actual DrawPile/replacement materials.");
            }
            else if (r.Stage != ZixiEffectsStage.Drawing) throw new InvalidOperationException("Zixi cannot finish before its typed native draw returns.");
        }
        if (r.JudgmentCount == 3)
        {
            if (r.Stage is not (ZixiEffectsStage.ObtainChildren or ZixiEffectsStage.DamageChildren) || r.DrawOutcome is not null)
                throw new InvalidOperationException("Zixi's Judgment/Equipment obtain cannot borrow the native draw branch.");
            if (r.ActualCount >= 0)
            {
                var moves = ProgramEventHistory<ZixiEffectsEntityObtainedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
                if (ProgramEventHistory<ZixiEffectsObtainPaidEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var paid] ||
                    paid != new ZixiEffectsObtainPaidEvent(f.Id, r.BatchId, moves.Length, r.ActualCount, r.Before, r.After) ||
                    r.BatchId > 0 && moves.Length != r.Entities.Count ||
                    r.BatchId == 0 && (moves.Length != 0 || r.ActualCount != 0 || r.Before != r.After) ||
                    r.ActualCount != moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)))
                    throw new InvalidOperationException("Zixi changed its once-obtained whole JE set or actual Hand destinations.");
                for (var i = 0; i < moves.Length; i++)
                {
                    var m = moves[i]; var entity = r.Entities[i];
                    if (m.Index != i || m.CardId != entity.CardId || m.PrintedKind != entity.PrintedKind || m.From != entity.From ||
                        m.BatchId != r.BatchId || m.Sequence <= r.Before || m.Sequence > r.After ||
                        _cardMovements.Count(x => x.CardId == m.CardId && x.CardKind == m.PrintedKind && x.From == m.From && x.To == m.To &&
                            x.Sequence == m.Sequence && x.Reason.Value == ZixiEffectsObtainReason) != 1 ||
                        m.To != CardLocation.Hand(f.OwnerSeat) && m.To != CardLocation.OutsideGame)
                        throw new InvalidOperationException("Zixi lost an original JE entity, true destination or once-only payment.");
                }
            }
            else if (r.Stage != ZixiEffectsStage.ObtainChildren || r.BatchId != 0)
                throw new InvalidOperationException("Zixi cannot cause damage before the real obtain batch.");
            if (r.Stage == ZixiEffectsStage.DamageChildren && (r.ActualCount < 0 ||
                ProgramEventHistory<ZixiEffectsDamageIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var damage] ||
                damage != new ZixiEffectsDamageIssuedEvent(f.Id, f.OwnerSeat, r.TargetSeat, 3)))
                throw new InvalidOperationException("Zixi needs its one original native Damage3 issuance after all obtain children.");
        }
        if (f.PendingMovementContinuation is { } pending && !IsZixiEffectsMovement(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Zixi's movement continuation lost its exact paid native instruction.");
    }
    private bool IsZixiEffectsMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.ZixiEffects is { Stage: ZixiEffectsStage.DrawChildren or ZixiEffectsStage.ObtainChildren } &&
        effect?.Op == SkillProgramEffectOp.ResolveZixiUniqueTargetJudgmentEffects && ValidZixiEffectsStarted(f) &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool ZixiEffectsPrimaryMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.ZixiEffects!;
        if (!_cardMovements.Contains(m)) return false;
        if (r.Stage == ZixiEffectsStage.ObtainChildren) return ProgramEventHistory<ZixiEffectsEntityObtainedEvent>().Any(e =>
            e.FrameId == f.Id && e.CardId == m.CardId && e.PrintedKind == m.CardKind && e.From == m.From && e.To == m.To &&
            e.Sequence == m.Sequence && e.BatchId == r.BatchId && m.Reason.Value == ZixiEffectsObtainReason);
        return r.Stage == ZixiEffectsStage.DrawChildren && r.DrawOutcome is { } o && m.Sequence > o.SequenceBefore && m.Sequence <= o.SequenceAfter &&
            (o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZixiEffectsDrawReason ||
             o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(f.OwnerSeat) ||
             m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool ZixiEffectsOxSpill(ProgramSkillFrame f, CardMovementBatchContext batch) => f.ZixiEffects is { Stage: ZixiEffectsStage.ObtainChildren, BatchId: > 0 } r &&
        batch.ParentBatchId == r.BatchId && batch.Movements.Count > 0 && batch.Movements.All(m =>
            m.From == CardLocation.WoodenOxGrain(r.TargetSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
            _cardMovements.Contains(m)) && r.Entities.Any(e => e.PrintedKind == CardKind.WoodenOx && e.From == CardLocation.Equipment(r.TargetSeat));
    private bool IsZixiEffectsRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.ZixiEffects is { Stage: ZixiEffectsStage.ObtainChildren } r && ValidZixiEffectsStarted(f) &&
        RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == ZixiEffectsObtainReason &&
        recovery.Attempt.SourceSeat == r.TargetSeat && recovery.Attempt.TargetSeat == r.TargetSeat && recovery.Attempt.Amount == 1 &&
        r.Entities.Any(e => e.PrintedKind == CardKind.SilverLion && e.From == CardLocation.Equipment(r.TargetSeat));
}
