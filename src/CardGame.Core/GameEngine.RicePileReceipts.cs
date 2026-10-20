namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void RequireRiceReceipt(ProgramSkillFrame f)
    {
        var r = f.RicePile ?? throw new InvalidOperationException("Rice lost its owning receipt.");
        if (!RicePileContract.IsOperation(r.Operation) || !ExactRiceParent(f, r.Operation) || r.Source != RiceIssuer(f) ||
            r.GameplayHash != f.GameplayHash || r.ParentId != f.WindowContext!.ParentFrameId || r.DamageId != f.WindowContext.DamageFrameId ||
            r.OccurrenceIndex != f.WindowContext.OccurrenceIndex || r.ActualTurn != _turnNumber || !Enum.IsDefined(r.Stage) ||
            !RiceProducer(r.Pile) || r.Pile.OwnerSeat != f.OwnerSeat || !IsValidPlayerSeat(r.BeneficiarySeat) ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.CardIds.Distinct().Count() != r.CardIds.Count || r.From.Count != r.CardIds.Count ||
            r.DrawActual is < 0 or > 2 || r.StoreRequired is < 0 or > 2 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.DrawBefore < 0 || r.DrawAfter < r.DrawBefore || r.DrawAfter > _movementSequence)
            throw new InvalidOperationException("Rice lost its exact source, actual parent, damage occurrence or entity invoice.");
        var events = CompleteProgramEventHistory().ToArray();
        if (events.OfType<RicePileStartedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Receipt.Operation == r.Operation &&
            e.Receipt.Source == r.Source && e.Receipt.GameplayHash == r.GameplayHash && e.Receipt.ParentId == r.ParentId &&
            e.Receipt.DamageId == r.DamageId && e.Receipt.OccurrenceIndex == r.OccurrenceIndex && e.Receipt.ActualTurn == r.ActualTurn &&
            e.Receipt.BeneficiarySeat == r.BeneficiarySeat) != 1)
            throw new InvalidOperationException("Rice was not begun exactly once from this actual candidate.");
        if (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
        {
            if (r.Pile.SkillId != f.SkillId || r.Pile.SkillInstanceId != f.SkillInstanceId || r.BeneficiarySeat != f.OwnerSeat ||
                r.Stage == RicePileStage.RecoveryChildren || r.RecoveryIssued ||
                events.OfType<RicePileDrawIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Requested == 2 && e.Actual == r.DrawActual &&
                    e.Before == r.DrawBefore && e.After == r.DrawAfter) != 1 ||
                _cardMovements.Count(m => m.Sequence > r.DrawBefore && m.Sequence <= r.DrawAfter && m.Reason.Value == RiceDrawReason &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat)) != r.DrawActual)
                throw new InvalidOperationException("Rice ending must match its once-issued actual draw before storing owned cards.");
        }
        else if (r.DrawActual != 0 || r.DrawBefore != 0 || r.DrawAfter != 0 || r.StoreRequired != 0 || r.Selected.Count != 0)
            throw new InvalidOperationException("A rice consumer retained an unrelated ending draw or selection.");
        if (r.Stage == RicePileStage.RecoveryChildren)
        {
            if (r.Operation != SkillProgramEffectOp.RecoverAfterLastPublicRice || !r.RecoveryIssued || r.BeneficiarySeat != f.OwnerSeat ||
                r.Pile.SkillId != f.SkillId || r.Pile.SkillInstanceId != f.SkillInstanceId || f.PendingMovementContinuation is not null ||
                events.OfType<RicePileRecoveryIssuedEvent>().Count(e => e == new RicePileRecoveryIssuedEvent(f.Id,
                    f.WindowContext.MovementBatch!.Id, r.Source, r.Pile)) != 1 || r.CardIds.Count != 0 || r.Choices.Count != 0)
                throw new InvalidOperationException("Rice recovery requires one actual exact-pile exhaustion and one issued recovery.");
            return;
        }
        if (r.RecoveryIssued || events.OfType<RicePileRecoveryIssuedEvent>().Any(e => e.ProgramFrameId == f.Id))
            throw new InvalidOperationException("An ending or rice payment cannot manufacture an exhaustion recovery.");
        if (r.Stage == RicePileStage.StoreSelection)
        {
            if (r.CardIds.Count != 0 || f.PendingMovementContinuation is not null || r.Selected.Count > r.StoreRequired ||
                r.Selected.Any(id => !RiceStorageCards(f).Any(c => c.Id == id)) ||
                events.OfType<RicePilePaymentIssuedEvent>().Any(e => e.ProgramFrameId == f.Id))
                throw new InvalidOperationException("Unpaid rice selection retained paid cards or lost current owned material.");
            if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } prompt &&
                (prompt.Kind != DecisionKind.ProgramTrigger || prompt.PlayerSeat != r.BeneficiarySeat || prompt.TargetSeat != r.BeneficiarySeat ||
                 prompt.IsPrivate != (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice) || !AssistedChoicesEqual(prompt.Choices, r.Choices)))
                throw new InvalidOperationException("Rice prompt lost its actual chooser or private owned-card boundary.");
            return;
        }
        if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } pending || pending.SubjectSeat != r.BeneficiarySeat ||
            r.Selected.Count != 0 || r.Choices.Count != 0)
            throw new InvalidOperationException("An issued rice movement must wait for its typed parent return.");
        if (r.Stage == RicePileStage.DrawChildren)
        {
            if (r.Operation != SkillProgramEffectOp.DrawAndStoreTwoPublicRice || r.CardIds.Count != 0) throw new InvalidOperationException("Only rice ending owns a draw child.");
            return;
        }
        if (r.Stage != RicePileStage.MovementChildren ||
            events.OfType<RicePilePaymentIssuedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray() is not [var paid] ||
            paid.Receipt.Operation != r.Operation || paid.Receipt.Source != r.Source || paid.Receipt.ParentId != r.ParentId || paid.Receipt.Pile != r.Pile ||
            paid.Receipt.BeneficiarySeat != r.BeneficiarySeat || paid.Receipt.Before != r.Before || paid.Receipt.After != r.After ||
            !paid.Receipt.CardIds.SequenceEqual(r.CardIds) || !paid.Receipt.From.SequenceEqual(r.From))
            throw new InvalidOperationException("Rice payment no longer matches its immutable once-issued invoice.");
        if (r.Operation == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice)
        {
            var context = f.WindowContext.JudgmentReplacement!;
            if (r.CardIds is not [var card] || r.From is not [var from] || from != r.Pile.Location || card != context.ReplacementCardId ||
                context.ReplacementSuit is null || context.ReplacementRank is null ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == context.OldCardId &&
                    m.From == CardLocation.Judgment(r.BeneficiarySeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ProgramJudgmentOldCard) != 1 ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == card && m.From == from &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.ProgramJudgmentReplace) != 1 ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == card && m.From == CardLocation.Processing &&
                    m.To == CardLocation.Judgment(r.BeneficiarySeat) && m.Reason == CardMoveReasons.ProgramJudgmentReplace) != 1)
                throw new InvalidOperationException("Rice judgment payment must discard its old card and move one exact rice through Processing into Judgment exactly once.");
            return;
        }
        var store = r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice;
        if (store ? r.CardIds.Count != r.StoreRequired : r.Operation != SkillProgramEffectOp.OfferRiceAfterDamagePoint || r.CardIds.Count != 1 ||
                r.BeneficiarySeat != f.WindowContext.TargetSeat)
            throw new InvalidOperationException("Rice transfer lost its exact ending cost or damage beneficiary.");
        var reason = store ? RiceStoreReason : RiceGainReason; var to = store ? r.Pile.Location : CardLocation.Hand(r.BeneficiarySeat);
        foreach (var item in r.CardIds.Select((id, index) => (id, from: r.From[index])))
            if ((store ? item.from.OwnerSeat != f.OwnerSeat || item.from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) : item.from != r.Pile.Location) ||
                _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == item.id && m.From == item.from && m.To == to && m.Reason.Value == reason) != 1)
                throw new InvalidOperationException("Rice transfer must match each actual paid physical entity and its precise source pile.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 450)]
    private void AssertRegisteredRiceReceipt(ProgramSkillFrame frame)
    {
        if (frame.RicePile is not null) RequireRiceReceipt(frame);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 40)]
    private bool IsRiceMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p)
    {
        if (f.RicePile is not { Stage: RicePileStage.DrawChildren or RicePileStage.MovementChildren } r || effect?.Op != r.Operation ||
            p != f.PendingMovementContinuation || p.SubjectSeat != r.BeneficiarySeat || p.BeforeCount != 0 || p.CoverageResultBind is not null) return false;
        RequireRiceReceipt(f); return true;
    }
}
