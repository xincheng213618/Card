namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool TangTangMoving(TangTangStage stage) => stage is TangTangStage.TakeChildren or TangTangStage.GiftChildren or TangTangStage.DrawChildren;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27530)]
    private void AssertTangTang(ProgramSkillFrame f)
    {
        if (f.TangTang is not { } r) return;
        if (f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != TangTangSource(f) || r.GameplayHash != f.GameplayHash || !TangTangComposition.Handles(r.Op) || GetPausedPrivateOfferEffect(f)?.Op != r.Op ||
            r.ActualTurn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat || !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Count > 2 || r.Materials.Select(c => c.CardId).Distinct().Count() != r.Materials.Count || r.Candidates.Select(c => c.CardId).Distinct().Count() != r.Candidates.Count || r.Viewed.Distinct().Count() != r.Viewed.Count || r.Grain.Distinct().Count() != r.Grain.Count ||
            ProgramEventHistory<TangTangStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] || start != new TangTangStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActualTurn, r.TurnOwner, r.WindowId, r.PlayPhase?.Token ?? 0))
            throw new InvalidOperationException("Tang Tang lost her exact original source, instruction, actual turn or immutable started receipt.");
        if (r.Op == SkillProgramEffectOp.ResolveZexingShownCardChain)
        {
            if (f.TriggerId is not null || f.ActivationId != "shown-card-chain" || f.WindowContext is not null || r.WindowId != 0 || r.PlayPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || phase.TurnNumber != r.ActualTurn || phase.ActualTurnOwnerSeat != r.TurnOwner || !IsRecordedActualDiscardRecoveryPhase(phase) || r.Gains.Count != 0 || r.Stage is TangTangStage.BenefitTarget or TangTangStage.DrawChildren)
                throw new InvalidOperationException("Zexing lost its actual Play activation origin.");
            var shown = ProgramEventHistory<ZexingShownEvent>().Where(e => e.FrameId == f.Id).ToArray();
            if (r.Paid ? r.Shown is not { } card || shown is not [var fact] || fact != new ZexingShownEvent(f.Id, f.OwnerSeat, card.CardId, card.Kind, card.From, card.Suit) : shown.Length != 0 || r.Stage is not (TangTangStage.ShownCard or TangTangStage.FirstPeer))
                throw new InvalidOperationException("Zexing cannot substitute the original shown entity or issue an unpaid chain.");
            if (r.Paid && (r.Peer < 0 || r.Peer == f.OwnerSeat || !IsValidPlayerSeat(r.Peer) || ProgramEventHistory<ZexingPeerChosenEvent>().Count(e => e.FrameId == f.Id && e.Peer == r.Peer) != 1 || ProgramEventHistory<ZexingPeerChosenEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner && e.Peer == r.Peer) != 1))
                throw new InvalidOperationException("Zexing cannot revisit an already chosen peer in the same actual turn.");
            if (r.Stage == TangTangStage.TakeCards && (r.Candidates.Count < 2 || r.Shown is null || r.Candidates.Any(c => c.From.OwnerSeat != r.Peer || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || c.Suit == r.Shown.Suit) || r.Selected.Any(id => !r.Candidates.Any(c => c.CardId == id)) || ProgramEventHistory<ZexingHandViewedEvent>().Count(e => e.FrameId == f.Id && e.Peer == r.Peer && e.ViewerSeat == f.OwnerSeat && e.Count == r.Viewed.Count) != 1))
                throw new InvalidOperationException("Zexing lost its issuer-only hand view or exact two-card suit filter.");
            if (r.Stage is TangTangStage.HpIssuing or TangTangStage.HpChildren)
            {
                if (ProgramEventHistory<ZexingOptionChosenEvent>().Count(e => e.FrameId == f.Id && e.Peer == r.Peer && e.Option == 2) != 1) throw new InvalidOperationException("Zexing HP loss must come from that peer's one actual choice.");
                if (r.Stage == TangTangStage.HpChildren && (ProgramEventHistory<ZexingHpLossIssuedEvent>().Where(e => e.FrameId == f.Id && e.Peer == r.Peer).ToArray() is not [var hp] || hp.Before - hp.After != 1 || !ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.SkillId == f.SkillId && e.TargetSeat == r.Peer && e.Amount == 1 && e.RemainingHp == hp.After)))
                    throw new InvalidOperationException("Zexing lost its real native HP payment.");
            }
        }
        else
        {
            if (!r.Paid || f.TriggerId != "gain-color" || f.WindowContext is not { } c || r.WindowId != c.ParentFrameId || r.PlayPhase is not null || r.Shown is not null || r.Stage is not (TangTangStage.BenefitTarget or TangTangStage.DrawChildren) || CurrentTangTangGains(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, c.OccurrenceIndex), c) is not { } gains || !r.Gains.SequenceEqual(gains) ||
                ProgramEventHistory<ZhiyiOpportunityPaidEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner && e.BatchId == r.WindowId) != 1 || ProgramEventHistory<ZhiyiOpportunityPaidEvent>().Count(e => e.OwnerSeat == f.OwnerSeat && e.ActualTurn == r.ActualTurn && e.TurnOwner == r.TurnOwner) != 1)
                throw new InvalidOperationException("Zhiyi lost its frozen whole-gain qualification or shared actual-turn payment.");
        }
        if (TangTangMoving(r.Stage))
        {
            if (!r.Paid || f.PendingMovementContinuation is not { } p || !IsTangTangMovement(f, GetPausedPrivateOfferEffect(f), p) || ProgramEventHistory<TangTangMovementIssuedEvent>().Where(e => e.FrameId == f.Id && e.Step == r.Step).ToArray() is not [var move] || move != new TangTangMovementIssuedEvent(f.Id, r.Step, r.Stage, r.Before, r.After, r.ActualCount))
                throw new InvalidOperationException("Tang Tang lost her exact once-issued native movement continuation.");
            if (r.Stage == TangTangStage.DrawChildren)
            {
                if (r.ActualCount is < 0 or > 1 || !IsValidPlayerSeat(r.Recipient) || ProgramEventHistory<ZhiyiRecipientChosenEvent>().Count(e => e.FrameId == f.Id && e.RecipientSeat == r.Recipient) != 1) throw new InvalidOperationException("Zhiyi cannot draw more than one actual card or substitute the selected recipient.");
            }
            else
            {
                if (r.Materials.Count != r.ActualCount || r.ActualCount != (r.Stage == TangTangStage.TakeChildren ? 2 : 1) || !ProgramEventHistory<TangTangMovementMaterialEvent>().Where(e => e.FrameId == f.Id && e.Step == r.Step).OrderBy(e => e.Index).Select(e => new TangTangEntity(e.CardId, e.Kind, e.From, e.Suit)).SequenceEqual(r.Materials) || !ProgramEventHistory<TangTangGrainEvent>().Where(e => e.FrameId == f.Id && e.Step == r.Step).OrderBy(e => e.Index).Select(e => e.CardId).SequenceEqual(r.Grain))
                    throw new InvalidOperationException("Zexing lost its frozen primary material set or Ox auxiliaries.");
                var paid = ProgramEventHistory<TangTangEntityMovedEvent>().Where(e => e.FrameId == f.Id && e.Step == r.Step).ToArray();
                if (paid.Length != r.Materials.Count || paid.Any(e => e.Stage != r.Stage || e.Sequence <= r.Before || e.Sequence > r.After || !r.Materials.Any(c => c.CardId == e.CardId && c.Kind == e.Kind && c.From == e.From) || !_cardMovements.Any(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == e.From && m.To == e.To && m.Reason.Value == (r.Stage == TangTangStage.TakeChildren ? ZexingTakeReason : ZexingGiftReason)) || e.To != CardLocation.Hand(r.Stage == TangTangStage.TakeChildren ? f.OwnerSeat : r.Peer)))
                    throw new InvalidOperationException("Zexing requires actual full take2 and original-card transfer movements.");
                if (r.Stage == TangTangStage.GiftChildren && paid.Single().CardId != r.Shown?.CardId) throw new InvalidOperationException("Zexing cannot gift a replacement for its shown physical card.");
            }
        }
        else if (f.PendingMovementContinuation is not null && r.Stage is not (TangTangStage.HpIssuing or TangTangStage.HpChildren)) throw new InvalidOperationException("Tang Tang has no unowned movement continuation.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 460)]
    private bool IsTangTangMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.TangTang is { Paid: true } r && e?.Op == r.Op && p == f.PendingMovementContinuation && p.BeforeCount == 0 && p.CoverageResultBind is null &&
        (TangTangMoving(r.Stage) && p.SubjectSeat == (r.Stage == TangTangStage.TakeChildren ? r.Peer : r.Stage == TangTangStage.DrawChildren ? r.Recipient : f.OwnerSeat) || r.Stage is TangTangStage.HpIssuing or TangTangStage.HpChildren && p.SubjectSeat == r.Peer);
    private bool IsTangTangPaidMovement(ProgramSkillFrame f, CardMovementRecord m)
    {
        var r = f.TangTang!;
        if (!_cardMovements.Contains(m)) return false;
        if (r.Stage == TangTangStage.HpChildren) return m.Sequence > r.Before && m.From.OwnerSeat == r.Peer && m.To == CardLocation.DiscardPile && m.Reason is var reason && (reason == CardMoveReasons.DeathDiscard || reason == CardMoveReasons.WoodenOxGrainDiscard) && !_players[r.Peer].IsAlive && ProgramEventHistory<ZexingHpLossIssuedEvent>().Any(e => e.FrameId == f.Id && e.Peer == r.Peer && e.After == 0);
        if (m.Sequence <= r.Before || m.Sequence > r.After) return false;
        if (r.Stage == TangTangStage.DrawChildren) return m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.Recipient) && m.Reason.Value == ZhiyiDrawReason || m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
        if (ProgramEventHistory<TangTangEntityMovedEvent>().Any(e => e.FrameId == f.Id && e.Step == r.Step && e.Stage == r.Stage && e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.To == m.To && e.Sequence == m.Sequence)) return true;
        return r.Grain.Contains(m.CardId) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard && r.Materials.Any(c => c.Kind == CardKind.WoodenOx && c.From.Zone == CardZoneKind.Equipment && m.From == CardLocation.WoodenOxGrain(c.From.OwnerSeat!.Value));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 210)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 200)]
    private bool IsTangTangRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.TangTang is { Paid: true } r && r.Stage is TangTangStage.TakeChildren or TangTangStage.GiftChildren && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
        recovery.Attempt.Completion.MoveReason?.Value == (r.Stage == TangTangStage.TakeChildren ? ZexingTakeReason : ZexingGiftReason) && r.Materials.Any(c => c.Kind == CardKind.SilverLion && c.From.Zone == CardZoneKind.Equipment && c.From.OwnerSeat == recovery.Attempt.TargetSeat);
    private bool TangTangFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.TangTang is not { Paid: true } r) return false;
        AssertTangTang(f);
        if (child is IdentityAppearanceFrame appearance) return r.Stage == TangTangStage.HpIssuing && appearance.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss && appearance.Receipt.ParentFrameId == f.Id && appearance.Receipt.OwnerSeat == r.Peer && appearance.Receipt.RequestedDeduction == 1 && appearance.Receipt.OriginalInstructionIndex == f.InstructionIndex;
        if (child is DyingFrame dying) return r.Stage == TangTangStage.HpChildren && dying.ParentFrameId == f.Id && dying.VictimSeat == r.Peer && dying.Continuation == DyingContinuationKind.ProgramSkill && dying.KillerSeat is null && ProgramEventHistory<ZexingHpLossIssuedEvent>().Any(e => e.FrameId == f.Id && e.Peer == r.Peer && e.After == 0);
        if (child is RecoveryReplacementFrame recovery) return IsTangTangRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp)
        {
            if (hp.Change.ParentFrameId != f.Id || hp.ResumeFrameId != f.Id) return false;
            if (r.Stage == TangTangStage.HpChildren) return hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Loss && hp.Change.SourceSeat is null && hp.Change.TargetSeat == r.Peer && hp.Change.Amount == 1;
            return TangTangMoving(r.Stage) && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == hp.Change.TargetSeat && hp.Change.Amount == 1 && r.Materials.Any(c => c.Kind == CardKind.SilverLion && c.From.Zone == CardZoneKind.Equipment && c.From.OwnerSeat == hp.Change.TargetSeat);
        }
        if (child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills) return skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange) return state.ResumeProgramFrameId == f.Id && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id);
        return child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null && ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => IsTangTangPaidMovement(f, m));
    }
    private ProgramSkillFrame? TangTangObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.TangTangFirstChild(f, c), static (e, i) => e.HasObserverSuffix(i,
        static (g, n) => g.IsMovementObserverEdge(n) || g.IsYanPuDyingExitEdge(n) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n], g._resolutionStack[n - 1]), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 970)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1080)]
    private bool HasTangTangDamageObserver(long id) => TangTangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1110)]
    private bool HasTangTangBeforeDamageObserver(long id) => TangTangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 940)]
    private bool IsTangTangDying() => ActiveDying is { } d && TangTangObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1510)]
    private bool AllowsTangTangNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || TangTangObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 27530)] private bool AdvanceTangTangChildren() => _pendingDecision is null && TangTangObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private IEnumerable<CardSnapshot> TangTangPrivatelyViewedCards(int viewerSeat) => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.OwnerSeat == viewerSeat && f.TangTang?.Stage == TangTangStage.TakeCards)
        .SelectMany(f => f.TangTang!.Viewed.Where(id => _cardZones.Contains(CardLocation.Hand(f.TangTang.Peer), id))).Distinct().Select(id => ToSnapshot(GetAdvancedCard(id)));
    private IEnumerable<int> TangTangPublicShownCardIds() => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.TangTang is { Paid: true, Shown: not null })
        .Where(f => _cardZones.Contains(CardLocation.Hand(f.OwnerSeat), f.TangTang!.Shown!.CardId) || _cardZones.Contains(CardLocation.Equipment(f.OwnerSeat), f.TangTang!.Shown!.CardId)).Select(f => f.TangTang!.Shown!.CardId).Distinct();
}
