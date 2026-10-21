namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MatchesOrdinaryBingyiRoot(ProgramSkillFrame f)
    {
        if (f.OrdinaryBingyi is not { Paid: true } r || f.SkillId != "ol:bingyi" || f.TriggerId != "ordinary-discard-reveal-share" || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } c || r.Source != OrdinaryGuYongSource(f) || r.GameplayHash != f.GameplayHash || r.WindowId != c.ParentFrameId || r.BatchId != c.MovementBatch?.Id || r.Phase != c.MovementBatch?.DiscardRecoveryPhase || r.OccurrenceIndex != c.OccurrenceIndex || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || !OrdinaryBingyiMovementRoot(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c)) return false;
        return ProgramEventHistory<OrdinaryBingyiShownEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var shown] && shown == new OrdinaryBingyiShownEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.WindowId, r.BatchId, r.OccurrenceIndex, r.ShownCards.Count, r.UniformColor, r.Color) && ProgramEventHistory<OrdinaryBingyiShownEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillId == f.SkillId && e.Phase == r.Phase) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27598)]
    private void AssertOrdinaryBingyi(ProgramSkillFrame f)
    {
        if (f.OrdinaryBingyi is not { } r) return;
        if (!MatchesOrdinaryBingyiRoot(f) || !Enum.IsDefined(r.Stage) || r.ShownCards is not System.Collections.IList { IsReadOnly: true } || r.OtherRecipients is not System.Collections.IList { IsReadOnly: true } || r.Recipients is not System.Collections.IList { IsReadOnly: true } || r.ShownCards.Count < 1 || r.ShownCards.Select(c => c.CardId).Distinct().Count() != r.ShownCards.Count || r.OtherRecipients.Count > r.ShownCards.Count || r.OtherRecipients.Distinct().Count() != r.OtherRecipients.Count || r.OtherRecipients.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat) || r.Recipients.Distinct().Count() != r.Recipients.Count || r.Cursor < 0 || r.Cursor > r.Recipients.Count)
            throw new InvalidOperationException("Ordinary Bingyi lost its immutable phase payment, original movement window or frozen hand.");
        var colors = r.ShownCards.Select(c => c.EffectiveSuit is Suit.Heart or Suit.Diamond ? (bool?)true : c.EffectiveSuit is Suit.Club or Suit.Spade ? false : null).Distinct().ToArray();
        var entities = ProgramEventHistory<OrdinaryBingyiShownEntityEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.UniformColor != (colors.Length == 1) || r.Color != (colors.Length == 1 ? colors[0] : null) || entities.Length != r.ShownCards.Count || !entities.Select((e, i) => e == new OrdinaryBingyiShownEntityEvent(f.Id, i, r.ShownCards[i].CardId, r.ShownCards[i].PrintedKind, r.ShownCards[i].EffectiveSuit)).All(v => v) || f.CardSetBindings.SingleOrDefault(b => b.Name == OrdinaryBingyiShownBind) is not { Visibility: SkillProgramCardSetVisibility.Public } bind || !bind.CardIds.SequenceEqual(r.ShownCards.Select(c => c.CardId)) || !bind.SourceLocations.All(l => l == CardLocation.Hand(f.OwnerSeat)))
            throw new InvalidOperationException("Ordinary Bingyi must retain exactly the hand and effective colors actually shown at payment.");
        var committed = ProgramEventHistory<OrdinaryBingyiRecipientsCommittedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var recipients = ProgramEventHistory<OrdinaryBingyiRecipientEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.RecipientsCommitted)
        {
            if (r.Stage != OrdinaryBingyiStage.SelectingRecipients || r.Recipients.Count != 0 || r.Cursor != 0 || r.DrawOutcome is not null || f.NativeDraw is not null || f.PendingMovementContinuation is not null || committed.Length != 0 || recipients.Length != 0) throw new InvalidOperationException("Ordinary Bingyi cannot draw before committing its one frozen recipient queue.");
            return;
        }
        if (!r.UniformColor || r.Stage == OrdinaryBingyiStage.SelectingRecipients || !r.Recipients.SequenceEqual(new[] { f.OwnerSeat }.Concat(r.OtherRecipients.OrderBy(s => RelativeTableOrder(r.Phase.ActualTurnOwnerSeat, s)))) || committed is not [var issued] || issued.Count != r.Recipients.Count || recipients.Length != r.Recipients.Count || !recipients.Select((e, i) => e == new OrdinaryBingyiRecipientEvent(f.Id, i, r.Recipients[i])).All(v => v)) throw new InvalidOperationException("Ordinary Bingyi changed its paid recipient count, order or identities.");
        var returned = ProgramEventHistory<OrdinaryBingyiDrawReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var skipped = ProgramEventHistory<OrdinaryBingyiRecipientSkippedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (returned.Any(e => e.DrawIndex < 0 || e.DrawIndex > r.Cursor || e.DrawIndex >= r.Recipients.Count || e.RecipientSeat != r.Recipients[e.DrawIndex]) || skipped.Any(e => e.DrawIndex < 0 || e.DrawIndex >= r.Cursor || e.RecipientSeat != r.Recipients[e.DrawIndex]) || Enumerable.Range(0, r.Cursor).Any(i => returned.Count(e => e.DrawIndex == i) + skipped.Count(e => e.DrawIndex == i) != 1)) throw new InvalidOperationException("Ordinary Bingyi repeated or skipped a recipient's already-paid native draw.");
        if (r.Stage == OrdinaryBingyiStage.DrawChildren)
        {
            if (r.Cursor >= r.Recipients.Count || r.DrawOutcome is not { } o || o.RecipientSeat != r.Recipients[r.Cursor] || o.RequestedCount != 1 || o.SequenceBefore < 0 || o.SequenceAfter < o.SequenceBefore || o.SequenceAfter > _movementSequence || returned.Where(e => e.DrawIndex == r.Cursor).ToArray() is not [var actual] || actual != new OrdinaryBingyiDrawReturnedEvent(f.Id, r.Cursor, o.RecipientSeat, o.ActualDrawIds.Count, o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId) || f.NativeDraw is not null || f.PendingMovementContinuation is not { } pending || !IsOrdinaryGuYongMovement(f, GetPausedPrivateOfferEffect(f), pending)) throw new InvalidOperationException("Ordinary Bingyi lost its native outcome or exact movement-child return.");
        }
        else if (r.DrawOutcome is not null || f.PendingMovementContinuation is not null || returned.Any(e => e.DrawIndex == r.Cursor)) throw new InvalidOperationException("Ordinary Bingyi retained a finished draw as a new request.");
    }
    private bool CanContinueIssuedOrdinaryGuYong(ProgramSkillFrame f)
    {
        if (f.OrdinaryBingyi is not { Paid: true }) return false;
        AssertOrdinaryBingyi(f); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27598)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27598)]
    private bool PermitPaidOrdinaryBingyi(ProgramSkillFrame f) => CanContinueIssuedOrdinaryGuYong(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 150)]
    private bool IsOrdinaryGuYongMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) => f.OrdinaryBingyi is { Paid: true, Stage: OrdinaryBingyiStage.DrawChildren, DrawOutcome: { } o } && effect?.Op == SkillProgramEffectOp.ResolveOrdinaryGuYongBingyi && p == f.PendingMovementContinuation && p.SubjectSeat == o.RecipientSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool IsOrdinaryBingyiDrawMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        if (f.OrdinaryBingyi is not { Paid: true, Stage: OrdinaryBingyiStage.DrawChildren, DrawOutcome: { } o } || !_cardMovements.Contains(m) || m.Sequence <= o.SequenceBefore || m.Sequence > o.SequenceAfter) return false;
        return o.ActualDrawIds.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(o.RecipientSeat) && m.Reason.Value == OrdinaryBingyiDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle || o.ReplacementGainedIds.Contains(m.CardId) && m.To == CardLocation.Hand(o.RecipientSeat);
    }
    private bool OrdinaryBingyiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.OrdinaryBingyi is not { Paid: true } r) return false;
        AssertOrdinaryBingyi(f);
        if (r.Stage == OrdinaryBingyiStage.Drawing && f.NativeDraw is not null) return NativeDrawAttemptRidesOn(child, f);
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsOrdinaryBingyiDrawMovement(f, m));
    }
    private bool OrdinaryBingyiObserverSuffix(int i) => HasObserverSuffix(i, static (g, n) => g.IsMovementObserverEdge(n) || g.IsSameNameHandObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]) || NativeDrawAttemptRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? OrdinaryBingyiObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.OrdinaryBingyiFirstChild(f, child), static (e, i) => e.OrdinaryBingyiObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1080)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1190)]
    private bool HasOrdinaryGuYongDamageObserver(long id) => OrdinaryBingyiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1220)]
    private bool HasOrdinaryGuYongBeforeDamageObserver(long id) => OrdinaryBingyiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 1050)]
    private bool IsOrdinaryGuYongDying() => ActiveDying is { } d && OrdinaryBingyiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1620)]
    private bool AllowsOrdinaryGuYongNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || OrdinaryBingyiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f); return effect is { Op: SkillProgramEffectOp.Damage } && effect.Amount == amount && effect.ActorReference == source && effect.DamageNature == nature && target == (effect.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 310)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 300)]
    private bool IsOrdinaryGuYongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.OrdinaryBingyi is not { Paid: true } || recovery.ParentFrameId == f.Id) return false;
        var i = _resolutionStack.FindIndex(p => p.Id == f.Id); var j = _resolutionStack.FindIndex(p => p.Id == recovery.Id);
        return i >= 0 && j > i + 1 && OrdinaryBingyiFirstChild(f, _resolutionStack[i + 1]) && OrdinaryBingyiObserverSuffix(i) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27598)]
    private bool AdvanceOrdinaryBingyiChildren() => _pendingDecision is null && OrdinaryBingyiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
