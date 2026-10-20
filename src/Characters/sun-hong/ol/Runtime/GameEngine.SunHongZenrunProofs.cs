namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool ZenrunChildren(ZenrunStage stage) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sun_hong_ol_SunHongZenrunProofs.ZenrunChildren(stage);
    private bool ValidZenrun(ProgramSkillFrame f)
    {
        if (f.Zenrun is not { } r || f.InstructionIndex != 1 || f.SkillId != "ol:zenrun" ||
            f.TriggerId != "replace-native-draw" || GetProgramBindingId(f) != "replace-native-draw" ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != ZenrunSource(f) ||
            r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement ||
            f.WindowContext is not { NativeDraw: { } draw } context || draw != r.Draw || context.ParentFrameId != r.WindowId ||
            ExactNativeDrawAttempt(context) is not { } attempt || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount <= 0 ||
            draw.ActualPhase is null || draw.ActualTurnNumber != _turnNumber || draw.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
            !IsValidPlayerSeat(r.SourceSeat) || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Revision < 0 || r.Step < 0 || r.Choice is < 0 or > 2 ||
            r.Selected.Select(s => (s.Zone, s.Index)).Distinct().Count() != r.Selected.Count ||
            r.Selected.Select(s => s.CardId).Distinct().Count() != r.Selected.Count || r.Selected.Count > draw.RequestedCount ||
            r.Selected.Any(s => s.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || s.Index < 0) ||
            ProgramEventHistory<ZenrunStartedEvent>().Count(e => e == new ZenrunStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, draw)) != 1)
            return false;
        var window = _resolutionStack.FirstOrDefault(x => x.Id == r.WindowId) as ProgramLifecycleTriggerWindowFrame;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (window is null || index < 1 || _resolutionStack[index - 1].Id != window.Id ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(f, window.Candidates[window.CandidateIndex])) return false;
        var targets = ProgramEventHistory<ZenrunSourceSelectedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Stage == ZenrunStage.Target)
            return !r.Paid && targets.Length == 0 && r.SourceSeat == f.OwnerSeat && r.Revision == 0 &&
                r.Selected.Count == 0 && r.Materials.Count == 0 && f.PendingMovementContinuation is null && attempt.Replacement is null;
        if (targets is not [var target] || target.SourceSeat != r.SourceSeat || r.SourceSeat == f.OwnerSeat) return false;
        var selected = new List<(CardZoneKind Zone, int Index)>();
        var edits = ProgramEventHistory<ZenrunSlotSelectedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (edits.Length != r.Revision) return false;
        for (var i = 0; i < edits.Length; i++)
        {
            var e = edits[i]; var slot = (e.Zone, e.Index);
            if (e.Revision != i + 1 || e.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || e.Index < 0 ||
                e.Selected == selected.Contains(slot)) return false;
            if (e.Selected) selected.Add(slot); else selected.Remove(slot);
        }
        if (!selected.SequenceEqual(r.Selected.Select(s => (s.Zone, s.Index)))) return false;
        var payments = ProgramEventHistory<ZenrunPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid) return r.Stage == ZenrunStage.Cards && payments.Length == 0 && r.Materials.Count == 0 &&
            r.Step == 0 && r.Choice == 0 && attempt.Replacement is null && f.PendingMovementContinuation is null &&
            r.Selected.All(s => _cardZones.CardsAt(new(s.Zone, r.SourceSeat)).ElementAtOrDefault(s.Index)?.Id == s.CardId);
        if (payments is not [var paid] || paid.Source != r.Source || paid.GameplayHash != r.GameplayHash ||
            paid.AttemptFrameId != draw.AttemptFrameId || paid.Phase != draw.ActualPhase || paid.SourceSeat != r.SourceSeat ||
            paid.Count != draw.RequestedCount || r.Selected.Count != draw.RequestedCount || r.Materials.Count != draw.RequestedCount ||
            !r.Materials.Select(m => m.CardId).SequenceEqual(r.Selected.Select(s => s.CardId)) ||
            r.Materials.Where((m, i) => m.From != new CardLocation(r.Selected[i].Zone, r.SourceSeat)).Any() ||
            ProgramEventHistory<ZenrunPaidEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId && e.Phase == paid.Phase) != 1)
            return false;
        var primary = ProgramEventHistory<ZenrunMaterialCommittedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (!primary.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, r.Materials.Count)) ||
            !primary.Select(e => new ZenrunMaterial(e.CardId, e.Kind, e.From)).SequenceEqual(r.Materials)) return false;
        var take = ProgramEventHistory<ZenrunMovementIssuedEvent>().Where(e => e.FrameId == f.Id && e.Stage == ZenrunStage.TakeChildren).ToArray();
        if (take is not [var cost] || cost.Step != 1 || cost.Count != draw.RequestedCount || cost.Before < draw.SequenceBefore || cost.After > _movementSequence ||
            attempt.Replacement is not { } replacement || replacement.OwnerSeat != f.OwnerSeat || replacement.SkillId != f.SkillId ||
            replacement.SkillInstanceId != f.SkillInstanceId || replacement.BindingId != f.ActivationId || replacement.SourceSeat != r.SourceSeat ||
            replacement.Reason.Value != ZenrunTakeReason || replacement.SequenceBefore != cost.Before || replacement.SequenceAfter != cost.After ||
            !replacement.GainedIds.SequenceEqual(r.Materials.Select(m => m.CardId))) return false;
        var moves = ProgramEventHistory<ZenrunEntityMovedEvent>().Where(e => e.FrameId == f.Id && e.Step == 1).ToArray();
        if (moves.Length != r.Materials.Count || moves.Any(e => !_cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId &&
            m.CardKind == e.Kind && m.From == e.From && m.To == e.To && m.Reason == e.Reason && ZenrunTakeMovementMatches(f, m, cost))) ||
            !moves.Select(e => e.CardId).SequenceEqual(r.Materials.Select(m => m.CardId))) return false;
        var choices = ProgramEventHistory<ZenrunVictimChoiceEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.Choice == 0 ? choices.Length != 0 : choices is not [var chosen] || chosen.VictimSeat != r.SourceSeat || chosen.Choice != r.Choice) return false;
        if (ZenrunChildren(r.Stage))
        {
            if (f.PendingMovementContinuation is not { } pending || !IsSunHongMovement(f, GetPausedPrivateOfferEffect(f), pending) ||
                ProgramEventHistory<ZenrunMovementIssuedEvent>().Count(e => e.FrameId == f.Id && e.Step == r.Step && e.Stage == r.Stage &&
                    e.Before == r.Before && e.After == r.After) != 1) return false;
            if (r.Stage == ZenrunStage.TakeChildren && (r.Before != cost.Before || r.After != cost.After || r.Step != 1 || r.Choice != 0)) return false;
            if (r.Stage == ZenrunStage.CompensationChildren)
            {
                var returned = ProgramEventHistory<ZenrunCompensationReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
                var drawn = ProgramEventHistory<ZenrunCompensationEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
                if (r.Choice != 1 || r.Step != 2 || returned is not [var invoice] || invoice.SequenceBefore != r.Before || invoice.SequenceAfter != r.After ||
                    invoice.ActualDrawCount != drawn.Length || drawn.Length > draw.RequestedCount ||
                    !drawn.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, drawn.Length)) || drawn.Select(e => e.CardId).Distinct().Count() != drawn.Length ||
                    drawn.Any(e => !_cardMovements.Any(m => m.CardId == e.CardId && ZenrunMovementMatches(f, m) && m.From == CardLocation.DrawPile))) return false;
            }
        }
        else if (f.PendingMovementContinuation is not null || r.Stage is not (ZenrunStage.VictimChoice or ZenrunStage.CompensationRequest)) return false;
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27571)]
    private void AssertZenrun(ProgramSkillFrame f)
    {
        if (f.Zenrun is not { } r) return;
        if (!ValidZenrun(f)) throw new InvalidOperationException("Zenrun lost its actual phase, native draw parent, opaque original HE slots or once-paid replacement receipt.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && r.Stage is ZenrunStage.Target or ZenrunStage.Cards or ZenrunStage.VictimChoice &&
            _pendingDecision is { } prompt && (prompt.PlayerSeat != (r.Stage == ZenrunStage.VictimChoice ? r.SourceSeat : f.OwnerSeat) ||
                prompt.SkillPrompt?.SkillId != f.SkillId || !AssistedChoicesEqual(prompt.Choices, ZenrunChoices(f))))
            throw new InvalidOperationException("Zenrun changed its actual chooser or exposed/replaced a private source slot.");
    }
    private bool ZenrunTakeMovementMatches(ProgramSkillFrame f, CardMovementRecord m, ZenrunMovementIssuedEvent paid) =>
        m.Sequence > paid.Before && m.Sequence <= paid.After && _cardMovements.Contains(m) &&
        (f.Zenrun!.Materials.Any(c => c.CardId == m.CardId && c.Kind == m.CardKind && c.From == m.From) && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZenrunTakeReason ||
            m.From == CardLocation.WoodenOxGrain(f.Zenrun.SourceSeat) && m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.WoodenOxGrainDiscard && f.Zenrun.Materials.Any(c => c.Kind == CardKind.WoodenOx && c.From == CardLocation.Equipment(f.Zenrun.SourceSeat)));
    private bool ZenrunMovementMatches(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.Zenrun!;
        if (!_cardMovements.Contains(m) || m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == ZenrunStage.TakeChildren)
            return r.Materials.Any(c => c.CardId == m.CardId && c.Kind == m.CardKind && c.From == m.From) && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == ZenrunTakeReason ||
                m.From == CardLocation.WoodenOxGrain(r.SourceSeat) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard &&
                r.Materials.Any(c => c.Kind == CardKind.WoodenOx && c.From == CardLocation.Equipment(r.SourceSeat));
        return r.Stage == ZenrunStage.CompensationChildren && (m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.SourceSeat) &&
            m.Reason.Value == ZenrunCompensationReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
    }
    private bool ZenrunFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Zenrun is not { Paid: true } r || !ValidZenrun(f)) return false;
        if (r.Stage == ZenrunStage.CompensationRequest) return f.NativeDraw is not null && NativeDrawAttemptRidesOn(child, f);
        if (!ZenrunChildren(r.Stage)) return false;
        if (child is RecoveryReplacementFrame recovery) return IsSunHongRecoveryParent(f, recovery);
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => ZenrunMovementMatches(f, m));
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange);
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 660)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 600)]
    private bool IsSunHongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.Zenrun is { Paid: true, Stage: ZenrunStage.TakeChildren } r && ValidZenrun(f) && RecoveryReplacementFrameRidesOn(recovery, f) &&
        recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.SourceSeat == r.SourceSeat &&
        recovery.Attempt.TargetSeat == r.SourceSeat && recovery.Attempt.Amount == 1 && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
        recovery.Attempt.Completion.MoveReason?.Value == ZenrunTakeReason && r.Materials.Any(c => c.Kind == CardKind.SilverLion && c.From == CardLocation.Equipment(r.SourceSeat));
}
