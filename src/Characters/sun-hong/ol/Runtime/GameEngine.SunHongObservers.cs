namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool XianbiChildren(XianbiStage stage) => stage is XianbiStage.DiscardChildren or XianbiStage.DrawChildren or XianbiStage.ObtainChildren;
    private bool ValidXianbi(ProgramSkillFrame f)
    {
        if (f.Xianbi is not { } r || f.InstructionIndex != 1 || f.SkillId != "ol:xianbi" ||
            f.TriggerId is not null || f.WindowContext is not null || GetProgramBindingId(f) != "adjust-and-return" ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != XianbiSource(f) ||
            r.GameplayHash != f.GameplayHash || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns ||
            !Enum.IsDefined(r.Stage) || r.ActualTurn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat ||
            !IsValidPlayerSeat(r.TargetSeat) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.Cursor < 0 || r.Cursor > r.Primary.Count || r.Hand.Select(c => c.CardId).Distinct().Count() != r.Hand.Count ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Any(id => !r.Hand.Any(c => c.CardId == id)) ||
            r.Primary.Select(c => c.CardId).Distinct().Count() != r.Primary.Count || r.Primary.Any(c => !r.Hand.Contains(c)) ||
            ProgramEventHistory<XianbiStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
                e.GameplayHash == r.GameplayHash && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner && e.PlayPhase == r.PlayPhase) != 1)
            return false;
        var target = ProgramEventHistory<XianbiTargetFrozenEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Issued) return r.Stage == XianbiStage.Target && target.Length == 0 && r.Hand.Count == 0 &&
            r.Primary.Count == 0 && r.Selected.Count == 0 && r.Revision == 0 && r.MovementStep == 0 && f.PendingMovementContinuation is null;
        if (target is not [var frozen] || frozen.TargetSeat != r.TargetSeat || frozen.HandBefore != r.HandBefore ||
            frozen.EquipmentCount != r.EquipmentCount || r.HandBefore != r.Hand.Count || r.HandBefore == r.EquipmentCount ||
            r.Required != Math.Abs(r.HandBefore - r.EquipmentCount) || r.Selected.Count > r.Required) return false;
        var hand = ProgramEventHistory<XianbiHandEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (!hand.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, r.Hand.Count)) ||
            !hand.Select(e => new XianbiEntity(e.CardId, e.Kind, e.EffectiveKind)).SequenceEqual(r.Hand)) return false;
        var selections = ProgramEventHistory<XianbiSelectionChangedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (selections.Length != r.Revision) return false;
        var selected = new List<int>();
        for (var i = 0; i < selections.Length; i++)
        {
            var change = selections[i];
            if (change.Revision != i + 1 || !r.Hand.Any(c => c.CardId == change.CardId) ||
                change.Selected == selected.Contains(change.CardId)) return false;
            if (change.Selected) selected.Add(change.CardId); else selected.Remove(change.CardId);
        }
        if (!selected.SequenceEqual(r.Selected)) return false;
        var committed = ProgramEventHistory<XianbiPrimaryCommittedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (!committed.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, r.Primary.Count)) ||
            !committed.Select(e => new XianbiEntity(e.CardId, e.Kind, e.EffectiveKind)).SequenceEqual(r.Primary)) return false;
        if (r.HandBefore < r.EquipmentCount)
        { if (r.Stage is not (XianbiStage.DrawRequest or XianbiStage.DrawChildren) || r.Primary.Count != 0 || r.Selected.Count != 0) return false; }
        else if (r.Stage != XianbiStage.DiscardChoice &&
            (r.Primary.Count != r.Required || !r.Primary.Select(c => c.CardId).SequenceEqual(r.Selected))) return false;
        if (XianbiChildren(r.Stage))
        {
            if (f.PendingMovementContinuation is not { } pending || !IsSunHongMovement(f, GetPausedPrivateOfferEffect(f), pending) ||
                r.MovementStep < 1 || ProgramEventHistory<XianbiMovementIssuedEvent>().Count(e => e.FrameId == f.Id &&
                    e.Step == r.MovementStep && e.Stage == r.Stage && e.Cursor == r.Cursor && e.Before == r.Before &&
                    e.After == r.After && e.Count == r.ActualCount) != 1) return false;
            var moves = ProgramEventHistory<XianbiEntityMovedEvent>().Where(e => e.FrameId == f.Id && e.Step == r.MovementStep).ToArray();
            if (r.Stage == XianbiStage.DrawChildren)
            {
                var drawn = ProgramEventHistory<XianbiDrawnEntityEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
                if (ProgramEventHistory<XianbiDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.SequenceBefore == r.Before &&
                    e.SequenceAfter == r.After && e.ActualDrawCount == r.ActualCount) != 1) return false;
                if (moves.Length != 0 || drawn.Length != r.ActualCount || drawn.Select(e => e.CardId).Distinct().Count() != drawn.Length ||
                    !drawn.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, drawn.Length)) || drawn.Length > r.Required ||
                    drawn.Any(e => !_cardMovements.Any(m => m.CardId == e.CardId && m.Sequence > r.Before && m.Sequence <= r.After &&
                        m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == XianbiDrawReason))) return false;
            }
            else if (moves.Length != r.ActualCount || moves.Any(e => !_cardMovements.Any(m => m.Sequence == e.Sequence &&
                m.CardId == e.CardId && m.CardKind == e.Kind && m.From == e.From && m.To == e.To && m.Reason == e.Reason && XianbiMovementMatches(f, m))) ||
                r.Stage == XianbiStage.DiscardChildren && r.ActualCount != r.Primary.Count ||
                r.Stage == XianbiStage.ObtainChildren && r.ActualCount != 1) return false;
        }
        else if (f.PendingMovementContinuation is not null) return false;
        if (r.Stage == XianbiStage.ObtainChildren)
        {
            if (r.Cursor >= r.Primary.Count || r.ChosenCard is not { } chosen || r.Eligible.Count == 0 ||
                r.Eligible.Distinct().Count() != r.Eligible.Count || r.Eligible.Contains(r.Primary[r.Cursor].CardId)) return false;
            var candidates = ProgramEventHistory<XianbiRandomCandidateEvent>().Where(e => e.FrameId == f.Id && e.Cursor == r.Cursor).OrderBy(e => e.Index).ToArray();
            var random = ProgramEventHistory<XianbiRandomIssuedEvent>().Where(e => e.FrameId == f.Id && e.Cursor == r.Cursor).ToArray();
            var replay = new DeterministicRandom(unchecked((int)r.RandomBefore)); var index = replay.Next(r.Eligible.Count);
            if (!candidates.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, r.Eligible.Count)) ||
                !candidates.Select(e => e.CardId).SequenceEqual(r.Eligible) || index != r.RandomIndex || replay.State != r.RandomAfter ||
                chosen != r.Eligible[index] || random is not [var issued] || issued.OriginalCardId != r.Primary[r.Cursor].CardId ||
                issued.CardId != chosen || issued.Index != index || issued.Count != r.Eligible.Count ||
                issued.Before != r.RandomBefore || issued.After != r.RandomAfter) return false;
        }
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27570)]
    private void AssertXianbi(ProgramSkillFrame f)
    {
        if (f.Xianbi is null) return;
        if (!ValidXianbi(f)) throw new InvalidOperationException("Xianbi lost its frozen role count, source, original primary entities, random proof or exact once-paid child stage.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && f.Xianbi.Stage is XianbiStage.Target or XianbiStage.DiscardChoice &&
            _pendingDecision is { } prompt && (prompt.PlayerSeat != f.OwnerSeat || prompt.SkillPrompt?.SkillId != f.SkillId ||
                !AssistedChoicesEqual(prompt.Choices, XianbiChoices(f))))
            throw new InvalidOperationException("Xianbi's private legal menu changed the actual original Hand choices.");
    }
    private bool IsSunHongMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.Xianbi is { Issued: true } r && XianbiChildren(r.Stage) &&
        effect?.Op == SkillProgramEffectOp.ResolveXianbiHandAdjustmentAndRandomReturns &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null ||
        f.Zenrun is { Paid: true } z && ZenrunChildren(z.Stage) && effect?.Op == SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement &&
        pending == f.PendingMovementContinuation && pending.SubjectSeat == z.SourceSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    private bool XianbiMovementMatches(ProgramSkillFrame f, CardMovementRecord move)
    {
        var r = f.Xianbi!;
        if (!_cardMovements.Contains(move) || move.Sequence <= r.Before || move.Sequence > r.After) return false;
        return r.Stage switch
        {
            XianbiStage.DiscardChildren => move.From == CardLocation.Hand(f.OwnerSeat) && move.To == CardLocation.DiscardPile &&
                move.Reason.Value == XianbiDiscardReason && r.Primary.Any(c => c.CardId == move.CardId && c.Kind == move.CardKind),
            XianbiStage.DrawChildren => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(f.OwnerSeat) &&
                move.Reason.Value == XianbiDrawReason || move.From == CardLocation.DiscardPile && move.To == CardLocation.DrawPile && move.Reason == CardMoveReasons.Reshuffle,
            XianbiStage.ObtainChildren => move.From == CardLocation.DiscardPile && move.To == CardLocation.Hand(f.OwnerSeat) &&
                move.Reason.Value == XianbiObtainReason && move.CardId == r.ChosenCard,
            _ => false
        };
    }
    private bool SunHongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.Zenrun is not null) return ZenrunFirstChild(f, child);
        if (f.Xianbi is { Issued: true, Stage: XianbiStage.DrawRequest } && f.NativeDraw is not null)
            return ValidXianbi(f) && NativeDrawAttemptRidesOn(child, f);
        if (f.Xianbi is not { Issued: true } r || !XianbiChildren(r.Stage) || !ValidXianbi(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
            moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => XianbiMovementMatches(f, m));
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } changed)
            return changed.ResumeProgramFrameId == f.Id && changed.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange;
    }
    private ProgramSkillFrame? SunHongObserverRoot() => FindProgramObserverRoot(
        static (engine, frame, child) => engine.SunHongFirstChild(frame, child),
        static (engine, index) => engine.HasObserverSuffix(index,
            static (e, at) => e.IsMovementObserverEdge(at) || e.IsYanPuDyingExitEdge(at) || e.RecoveryReplacementFrameRidesOn(e._resolutionStack[at], e._resolutionStack[at - 1]) ||
                NativeDrawAttemptRidesOn(e._resolutionStack[at], e._resolutionStack[at - 1]),
            static (e, at, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(at, dying)));
    private bool HasSunHongDamageObserver(long id) => SunHongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasSunHongBeforeDamageObserver(long id) => SunHongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasSunHongDyingObserver() => ActiveDying is { } dying && SunHongObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsSunHongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || SunHongObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27570)]
    private bool AdvanceSunHongChildren() => _pendingDecision is null && SunHongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
