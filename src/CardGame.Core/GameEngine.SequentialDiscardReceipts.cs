namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidSequentialDiscard(ProgramSkillFrame f)
    {
        if (f.SequentialDiscard is not { } d || f.TriggerId is not null || f.WindowContext is not null ||
            f.ActivationId is null || !f.ReexecuteParticipantInstruction ||
            !IsValidPlayerSeat(f.OwnerSeat) || f.OwnerSeat != d.ActualTurnOwnerSeat ||
            d.ActualTurnNumber != _turnNumber || d.ActualTurnOwnerSeat != _currentSeat ||
            d.Source != SequentialDiscardSource(f) || d.Source.OwnerSeat != f.OwnerSeat ||
            string.IsNullOrWhiteSpace(d.Source.SkillId) || string.IsNullOrWhiteSpace(d.Source.BindingId) ||
            string.IsNullOrWhiteSpace(d.Source.SkillInstanceId) || d.GameplayHash != f.GameplayHash ||
            f.SelectedTargetSeats is not [var start] || start != d.StartSeat || !IsValidPlayerSeat(start) || start == f.OwnerSeat ||
            _contentRegistry.Skills.GetValueOrDefault(f.SkillId)?.Program is not { } program || program.GameplayHash != f.GameplayHash ||
            program.Activations.SingleOrDefault(a => a.Id == f.ActivationId) is not { } activation ||
            f.InstructionIndex != d.InstructionIndex + 1 || d.InstructionIndex < 0 || d.InstructionIndex >= activation.Effects.Count ||
            !IsSequentialDiscardOp(activation.Effects[d.InstructionIndex].Op) ||
            d.Kind != (activation.Effects[d.InstructionIndex].Op == SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard
                ? ProgramSequentialDiscardKind.CategoryOrSequential : ProgramSequentialDiscardKind.SelectedStartEscalating) ||
            !d.Order.SequenceEqual(SequentialDiscardOrder(f.OwnerSeat, start, d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating)) ||
            d.Cursor < 0 || d.Cursor >= d.Order.Count || d.ChooserSeat != d.Order[d.Cursor] ||
            d.PreviousCount < 0 || d.Remaining < 0 || d.SelectedCardIds.Count != d.SelectedLocations.Count ||
            d.SelectedCardIds.Distinct().Count() != d.SelectedCardIds.Count ||
            d.SelectedLocations.Any(l => l.OwnerSeat != d.ChooserSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            !Enum.IsDefined(d.Stage) || d.Stage == ProgramSequentialDiscardStage.Complete)
            return false;
        var effect = activation.Effects[d.InstructionIndex];
        if (d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential)
        {
            if (d.InstructionIndex != 2 || activation.Effects is not
                [{ Op: SkillProgramEffectOp.CaptureSelectedCards, Target: SkillProgramEffectTarget.Owner, ResultBind: { } captured, Condition.Kind: SkillProgramConditionKind.Always },
                 { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: { } moved, Destination: SkillProgramCardDestination.DrawPileTop,
                   AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always }, { Op: SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard }] || captured != moved ||
                d.PreviousCount != 0 || d.Cursor != 0 || d.SelectedCardIds.Count != 0 ||
                d.Stage is ProgramSequentialDiscardStage.SelectingBatch or ProgramSequentialDiscardStage.AwaitingDamage ||
                !HasSequentialDiscardTopPayment(f, captured))
                return false;
        }
        else if (d.InstructionIndex != 0 || activation.Effects is not
            [{ Op: SkillProgramEffectOp.EscalatingDiscardOrDamageFromSelected, Target: SkillProgramEffectTarget.Owner }] || d.Remaining != 0 || d.PrimaryBranch == true ||
            d.Stage == ProgramSequentialDiscardStage.CardChoice) return false;
        if (d.Stage != ProgramSequentialDiscardStage.SelectingBatch && d.SelectedCardIds.Count != 0 ||
            d.Stage is ProgramSequentialDiscardStage.BranchChoice or ProgramSequentialDiscardStage.AwaitingDamage && d.PrimaryBranch is not null ||
            d.Stage is ProgramSequentialDiscardStage.CardChoice or ProgramSequentialDiscardStage.SelectingBatch or ProgramSequentialDiscardStage.AwaitingMovement && d.PrimaryBranch is null ||
            d.Stage == ProgramSequentialDiscardStage.SelectingBatch && d.SelectedCardIds.Where((id, n) =>
                _cardZones.GetLocation(id) != d.SelectedLocations[n] || !SequentialDiscardCards(f, d.ChooserSeat, effect)
                    .Any(item => item.Card.Id == id && item.Location == d.SelectedLocations[n])).Any()) return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (!history.OfType<ProgramSkillStartedEvent>().Any(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
            e.SkillId == f.SkillId && e.ActivationId == f.ActivationId)) return false;
        if (d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating &&
            (_skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, activation.UsageGroup, SkillUsageScope.Game) != 1 ||
             !history.OfType<SkillUsageConsumedEvent>().Any(e => e.SkillOwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId &&
                 e.UsageId == activation.UsageGroup && e.Scope == SkillUsageScope.Game && e.Count == 1))) return false;
        var issued = history.OfType<ProgramSequentialDiscardStartedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (issued.Length != 1 || issued[0] != new ProgramSequentialDiscardStartedEvent(f.Id, d.Kind, d.Source,
            d.GameplayHash, d.ActualTurnNumber, d.ActualTurnOwnerSeat, d.StartSeat)) return false;
        var branch = history.OfType<ProgramSequentialDiscardBranchEvent>().LastOrDefault(e => e.FrameId == f.Id && e.Cursor == d.Cursor);
        var paid = history.OfType<ProgramSequentialDiscardPaidEvent>().Where(e => e.FrameId == f.Id && e.Cursor == d.Cursor).ToArray();
        var priorCursor = history.OfType<ProgramSequentialDiscardPaidEvent>().Where(e => e.FrameId == f.Id && e.Cursor < d.Cursor).Select(e => e.Cursor)
            .Concat(history.OfType<ProgramSequentialDiscardDamageChosenEvent>().Where(e => e.FrameId == f.Id && e.Cursor < d.Cursor).Select(e => e.Cursor))
            .DefaultIfEmpty(-1).Max();
        var predecessorCount = priorCursor < 0 ? 0 : history.OfType<ProgramSequentialDiscardPaidEvent>()
            .LastOrDefault(e => e.FrameId == f.Id && e.Cursor == priorCursor)?.ActualCount ?? 0;
        if (d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating && d.PreviousCount !=
            (d.Stage == ProgramSequentialDiscardStage.AwaitingDamage ? 0 : d.Payment?.ActualCount ?? predecessorCount)) return false;
        if (d.PrimaryBranch is { } primary && (branch is null || branch.ChooserSeat != d.ChooserSeat || branch.PrimaryBranch != primary ||
            branch.RequiredCount != (d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential
                ? primary ? effect.Amount : effect.MinimumValue : predecessorCount + 1))) return false;
        if (d.Kind == ProgramSequentialDiscardKind.CategoryOrSequential && d.PrimaryBranch is not null &&
            d.Remaining != Math.Max(0, branch!.RequiredCount - paid.Sum(e => e.ActualCount))) return false;
        if (d.Stage == ProgramSequentialDiscardStage.AwaitingDamage)
        {
            if (d.PreviousCount != 0 || d.Payment is not null || f.PendingMovementContinuation is not null ||
                !history.OfType<ProgramSequentialDiscardDamageChosenEvent>().Any(e => e == new ProgramSequentialDiscardDamageChosenEvent(
                    f.Id, d.Cursor, f.OwnerSeat, d.ChooserSeat, effect.Amount, effect.DamageNature ?? DamageNature.Normal))) return false;
            // Chained damage may legitimately change TargetSeat after the original declared recipient.
            if (f.AttackAttempt is { } attempt && (attempt.SourceSeat != f.OwnerSeat || f.AttackReturn is null ||
                attempt.Nature != (effect.DamageNature ?? DamageNature.Normal))) return false;
        }
        else if (f.AttackAttempt is not null || f.AttackReturn is not null) return false;
        if (d.Payment is { } payment && !ValidSequentialDiscardPayment(f, d, payment)) return false;
        return d.Stage == ProgramSequentialDiscardStage.AwaitingMovement
            ? d.Payment is not null && f.PendingMovementContinuation is not null ||
                d.Payment is not null && ReferenceEquals(f, _resolutionStack.LastOrDefault())
            : f.PendingMovementContinuation is null;
    }

    private bool HasSequentialDiscardTopPayment(ProgramSkillFrame f, string bind)
    {
        if (f.SequentialDiscardTopPayment is not { InstructionIndex: 1 } p || p.ResultBind != bind ||
            p.Source != SequentialDiscardSource(f) || p.GameplayHash != f.GameplayHash || p.ActualTurnNumber != _turnNumber ||
            p.ActualTurnOwnerSeat != _currentSeat || p.ActualTurnOwnerSeat != f.OwnerSeat ||
            f.SelectedTargetSeats is not [var target] || p.TargetSeat != target ||
            f.SelectedCardIds is not [var id] || p.CardId != id || p.SourceLocation != CardLocation.Hand(f.OwnerSeat) ||
            f.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not
            { CardIds.Count: 1, SourceLocations.Count: 1 } captured || captured.CardIds[0] != id ||
            captured.SourceLocations[0] != CardLocation.Hand(f.OwnerSeat) ||
            !_cardZones.CardsAt(_cardZones.GetLocation(id)).Any(c => c.Id == id && GetProgramCardCategory(c.Kind) == SkillProgramCardCategory.Trick)) return false;
        return CompleteProgramEventHistory().OfType<ProgramSequentialDiscardTopPaymentIssuedEvent>().Any(e => e ==
            new ProgramSequentialDiscardTopPaymentIssuedEvent(f.Id, p.MovementSequenceBefore, p.Source, p.GameplayHash,
                p.ActualTurnNumber, p.ActualTurnOwnerSeat, p.TargetSeat)) &&
            _cardMovements.Count(m => m.Sequence > p.MovementSequenceBefore && m.CardId == id &&
                m.From == p.SourceLocation && m.To == CardLocation.DrawPile &&
                m.Reason.Value == $"skill-program.{f.SkillId}.{SkillProgramEffectOp.MoveBoundCards}") == 1;
    }

    // Called only at the existing MoveBoundCards producer, before physical payment.
    private ProgramSkillFrame FreezeSequentialDiscardTopPayment(ProgramSkillFrame f, string bind, string? except,
        SkillProgramCardDestination destination)
    {
        if (f.TriggerId is not null || f.WindowContext is not null ||
            _contentRegistry.Skills.GetValueOrDefault(f.SkillId)?.Program is not { } program ||
            !program.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard))) return f;
        var plan = ProgramInstructionResolver.Default.Resolve(f, program);
        if (f.InstructionIndex != 2 || plan.Instructions is not
            [{ Op: SkillProgramEffectOp.CaptureSelectedCards, Target: SkillProgramEffectTarget.Owner, ResultBind: { } capture, Condition.Kind: SkillProgramConditionKind.Always },
             { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, SourceBind: { } source, Destination: SkillProgramCardDestination.DrawPileTop,
               AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.Always }, { Op: SkillProgramEffectOp.ChooseCategoryOrSequentialDiscard }] ||
            capture != source || source != bind || except is not null || destination != SkillProgramCardDestination.DrawPileTop ||
            f.SequentialDiscardTopPayment is not null || f.SelectedTargetSeats is not [var target] ||
            f.SelectedCardIds is not [var id] || f.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not
            { CardIds.Count: 1, SourceLocations.Count: 1 } binding || binding.CardIds[0] != id ||
            binding.SourceLocations[0] != CardLocation.Hand(f.OwnerSeat) || _cardZones.GetLocation(id) != binding.SourceLocations[0])
            throw new InvalidOperationException("A sequential category challenge requires its exact original one-trick top payment.");
        var receipt = new ProgramSequentialDiscardTopPayment(1, bind, id, binding.SourceLocations[0],
            SequentialDiscardMovementSequence, SequentialDiscardSource(f), f.GameplayHash, _turnNumber, _currentSeat, target);
        ReplaceRuntimeTop(f = f with { SequentialDiscardTopPayment = receipt });
        AdvanceEventRulesAndQueueFact(new ProgramSequentialDiscardTopPaymentIssuedEvent(f.Id, receipt.MovementSequenceBefore,
            receipt.Source, receipt.GameplayHash, receipt.ActualTurnNumber, receipt.ActualTurnOwnerSeat, target));
        return f;
    }

    private bool ValidSequentialDiscardPayment(ProgramSkillFrame f, ProgramSequentialDiscardDraft d, ProgramSequentialDiscardPayment p)
    {
        if (p.ChooserSeat != d.ChooserSeat || p.Cursor != d.Cursor || p.CardIds.Count == 0 ||
            p.CardIds.Count != p.SourceLocations.Count || p.CardIds.Distinct().Count() != p.CardIds.Count ||
            p.ActualCount != p.CardIds.Count || p.SequenceBefore < 0 || p.SequenceAfter <= p.SequenceBefore ||
            p.SequenceAfter > SequentialDiscardMovementSequence ||
            p.SourceLocations.Any(l => l.OwnerSeat != d.ChooserSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var records = _cardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter).ToArray();
        if (records.Length != p.CardIds.Count || p.CardIds.Where((id, n) => records.Count(m => m.CardId == id &&
            m.From == p.SourceLocations[n] && m.Reason.Value == SequentialDiscardReason(f) &&
            (m.To == CardLocation.DiscardPile || m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.OutsideGame &&
             _cardZones.CardsAt(CardLocation.OutsideGame).Any(card => card.Id == id && card.IsGeneralWeapon))) != 1).Any()) return false;
        var paid = CompleteProgramEventHistory().OfType<ProgramSequentialDiscardPaidEvent>().Where(e => e.FrameId == f.Id &&
            e.Cursor == p.Cursor && e.ChooserSeat == p.ChooserSeat && e.SequenceBefore == p.SequenceBefore && e.SequenceAfter == p.SequenceAfter).ToArray();
        if (paid.Length != 1 || paid[0].SelectedCount != p.CardIds.Count || paid[0].ActualCount != p.ActualCount ||
            paid[0].RemainingAfter != d.Remaining || d.Kind == ProgramSequentialDiscardKind.SelectedStartEscalating && d.PreviousCount != p.ActualCount) return false;
        return f.PendingMovementContinuation is not { } continuation ||
            continuation.SubjectSeat == p.ChooserSeat && continuation.BeforeCount == 0 && continuation.CoverageResultBind is null;
    }

    private void AssertSequentialDiscard(ProgramSkillFrame f)
    {
        if (f.SequentialDiscardTopPayment is { } top && !HasSequentialDiscardTopPayment(f, top.ResultBind))
            throw new InvalidOperationException("A sequential discard lost its exact captured hand-to-top payment.");
        if (f.SequentialDiscard is not null && !ValidSequentialDiscard(f))
            throw new InvalidOperationException("A sequential discard lost its exact original activation, immutable private selection, payment ledger or actual turn.");
    }
}
