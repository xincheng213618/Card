namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool SameTypeAidParentMatches(ProgramSkillFrame f)
    {
        if (f.SameTypeAid is not { } r || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            r.Source != SameTypeAidSource(f) || r.GameplayHash != f.GameplayHash ||
            r.Use.OriginalOwnerTarget != f.OwnerSeat || !MatchesSameTypeAidUse(r.Use) ||
            f.WindowContext is not { } context || FreezeSameTypeAidUse(f.OwnerSeat, context) != r.Use) return false;
        var parent = _resolutionStack.SingleOrDefault(p => p.Id == context.ParentFrameId);
        if (parent is ProgramCardTriggerWindowFrame card)
            return card.ParentFrameId == r.Use.CardUseFrameId && card.CandidateIndex >= 0 && card.CandidateIndex < card.Candidates.Count &&
                MountObserverCandidateMatches(f, ToSharedCandidate(card.Candidates[card.CandidateIndex]));
        return parent is ActualUseTargetWindowFrame legacy && legacy.ParentFrameId == r.Use.CardUseFrameId &&
            legacy.CandidateIndex >= 0 && legacy.CandidateIndex < legacy.Candidates.Count &&
            MountObserverCandidateMatches(f, legacy.Candidates[legacy.CandidateIndex]) &&
            legacy.Contexts[legacy.CandidateIndex].ActualUseTarget == context.ActualUseTarget;
    }
    private bool IsValidSameTypeAidPayment(ProgramSkillFrame f)
    {
        if (f.SameTypeAid is not { RecipientSeat: { } donor, Payment: { } paid } r ||
            paid.From.OwnerSeat != donor || paid.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            !paid.Delivered || paid.CardId <= 0 || paid.MovementSequence <= 0 || paid.SequenceAfter < paid.MovementSequence ||
            !IsSameTypeAidCard(paid.PrintedKind, r.Use.EffectiveKind) ||
            _cardMovements.Count(m => m.Sequence == paid.MovementSequence && m.CardId == paid.CardId && m.CardKind == paid.PrintedKind &&
                m.From == paid.From && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == SameTypeAidGiftReason) != 1 ||
            CompleteProgramEventHistory().OfType<SameTypeAidGiftPaidEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.Source == r.Source && e.Use == r.Use && e.RecipientSeat == donor &&
                e.MovementSequence == paid.MovementSequence && e.SequenceAfter == paid.SequenceAfter && e.Delivered == paid.Delivered) != 1) return false;
        return !_cardMovements.Any(m => m.Sequence > paid.MovementSequence && m.Sequence <= paid.SequenceAfter &&
            !(paid.PrintedKind == CardKind.WoodenOx && paid.From == CardLocation.Equipment(donor) &&
              m.From == CardLocation.Grain(donor) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard));
    }
    private void AssertSameTypeAid(ProgramSkillFrame f)
    {
        if (f.SameTypeAid is not { } r) return;
        if (!SameTypeAidParentMatches(f) || !Enum.IsDefined(r.Stage) ||
            r.RecipientSeat is { } donor && (!IsValidPlayerSeat(donor) || donor == f.OwnerSeat || donor == r.Use.ActorSeat) ||
            r.Stage == SameTypeAidStage.ChoosingRecipient && (r.RecipientSeat is not null || r.Payment is not null || r.AddedTarget) ||
            r.Stage != SameTypeAidStage.ChoosingRecipient && r.RecipientSeat is null ||
            r.Payment is not null && (!IsValidSameTypeAidPayment(f) || r.AddedTarget) ||
            r.Stage == SameTypeAidStage.GiftChildren && r.Payment is null ||
            r.AddedTarget && (r.Stage != SameTypeAidStage.Complete || r.RecipientSeat is not { } extra ||
                CompleteProgramEventHistory().OfType<SameTypeAidTargetAddedEvent>().Count(e =>
                    e.ProgramFrameId == f.Id && e.Source == r.Source && e.Use == r.Use && e.RecipientSeat == extra) != 1))
            throw new InvalidOperationException("Aid changed its exact actual use, recipient or committed payment.");
    }
    private bool IsForeignContestOrAidMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        (effect?.Op == SkillProgramEffectOp.ResolveForeignTurnPindian && f.ForeignTurnContest is { Stage: ForeignTurnContestStage.ClaimChildren } &&
            pending.SubjectSeat == f.OwnerSeat ||
         effect?.Op == SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget && f.SameTypeAid is { Stage: SameTypeAidStage.GiftChildren, RecipientSeat: { } donor } &&
            pending.SubjectSeat == donor);

    private ProgramSkillFrame? ForeignContestAidPaidObserverRoot(long? exactDamageWindow = null)
    {
        for (var rootIndex = 1; rootIndex < _resolutionStack.Count; rootIndex++)
        {
            if (_resolutionStack[rootIndex] is not ProgramSkillFrame root) continue;
            int first, after, recoverySeat; int? paidId; CardLocation paidFrom; string reason;
            if (root.ForeignTurnContest is { Stage: ForeignTurnContestStage.ClaimChildren, ClaimedCardId: { } claimId, MovementSequence: { } sequence } contest &&
                MatchesForeignTurnContestOrigin(root, contest.SourceBind, contest.Origin))
            { first = after = sequence; recoverySeat = root.OwnerSeat; paidId = claimId; paidFrom = CardLocation.DiscardPile; reason = ForeignTurnContestClaimReason; }
            else if (root.SameTypeAid is { Stage: SameTypeAidStage.GiftChildren, RecipientSeat: { } donor, Payment: { } payment } &&
                SameTypeAidParentMatches(root) && IsValidSameTypeAidPayment(root))
            { first = payment.MovementSequence; after = payment.SequenceAfter; recoverySeat = donor; paidId = payment.CardId; paidFrom = payment.From; reason = SameTypeAidGiftReason; }
            else continue;
            if (exactDamageWindow is { } windowId && !_resolutionStack.Skip(rootIndex + 1).Any(f =>
                f.Id == windowId && f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)) continue;
            if (rootIndex == _resolutionStack.Count - 1) return root;
            if (root.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null }) continue;
            if (_resolutionStack[rootIndex + 1] is CardsMovedTriggerWindowFrame moved)
            {
                if (moved.Batch.ParentFrameId != root.Id || moved.Batch.OriginSkillId != root.SkillId ||
                    moved.Batch.OriginSkillInstanceId != root.SkillInstanceId || moved.Batch.OriginOwnerSeat != root.OwnerSeat ||
                    moved.Batch.AwaitingProgramFrameId is { } waiting && waiting != root.Id || moved.Batch.Movements.Count == 0 ||
                    moved.Batch.Movements.Any(m => m.Sequence < first || m.Sequence > after || !_cardMovements.Contains(m))) continue;
            }
            else
            {
                // An actual paid SilverLion removal can precede movement observers.
                if (paidFrom != CardLocation.Equipment(recoverySeat) || !_cardMovements.Any(m =>
                    m.Sequence == first && m.CardId == paidId && m.CardKind == CardKind.SilverLion &&
                    m.From == paidFrom && m.To == CardLocation.Hand(root.OwnerSeat) && m.Reason.Value == reason)) continue;
                if (_resolutionStack[rootIndex + 1] is HpChangedTriggerWindowFrame hp)
                {
                    if (hp.Change.ParentFrameId != root.Id || hp.ResumeFrameId != root.Id ||
                        hp.Continuation != PostEventContinuation.AwaitedProgramMovement || hp.Change.Kind != HpChangeKind.Recovery ||
                        hp.Change.SourceSeat != recoverySeat || hp.Change.TargetSeat != recoverySeat || hp.Change.Amount != 1) continue;
                }
                else if (_resolutionStack[rootIndex + 1] is RecoveryReplacementFrame recovery)
                {
                    if (!RecoveryReplacementFrameRidesOn(recovery, root) || recovery.Return.Continuation != PostEventContinuation.AwaitedProgramMovement ||
                        recovery.Attempt.SourceSeat != recoverySeat || recovery.Attempt.TargetSeat != recoverySeat || recovery.Attempt.Amount != 1 ||
                        recovery.Attempt.Completion.Producer != RecoveryAttemptProducer.SilverLion || recovery.Attempt.Completion.MoveReason?.Value != reason) continue;
                }
                else continue;
            }
            var valid = true;
            for (var i = rootIndex + 1; i < _resolutionStack.Count; i++)
            {
                // Root and first ledger edge have already been proved. No global
                // movement/Dying helper is widened by this local union.
                if (!PaidColorDamageClaimObserverEdge(i)) { valid = false; break; }
                if (_resolutionStack[i] is DyingFrame d && (IsPaidHandRepaymentRescueRide(i, d) ||
                    IsPaidHandRepaymentProgramAlcoholRide(i, d) || PolicyCounterspellVirtualAlcoholRide(i, d) ||
                    PaidObserverDamageVirtualAlcoholRide(i, d))) break;
            }
            if (valid) return root;
        }
        return null;
    }
    private bool HasForeignContestAidDamageObserver(long window) => ForeignContestAidPaidObserverRoot(window) is not null;
    private bool IsForeignContestAidProgramDying() => ActiveDying is { } dying &&
        ForeignContestAidPaidObserverRoot(ActiveDamageTrigger?.Id) is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasSameTypeAidTargetObserver(long useId)
    {
        if (_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SameTypeAid?.Use.CardUseFrameId == useId) is not { } root ||
            !SameTypeAidParentMatches(root)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        return index == _resolutionStack.Count - 1 || ForeignContestAidPaidObserverRoot()?.Id == root.Id;
    }
    private bool TryGetSameTypeAidVirtualPrimaryReturn(CardUseFrame use, ProgramSkillFrame parent, out IReadOnlyList<int> primary)
    {
        primary = [];
        if (!HasSameTypeAidTargetTail(use) || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            use.CardKind != CardKind.Slash || use.CardAttack is not
                { CardId: null, EffectiveCardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } attack ||
            attack.ProgramSkillCardUseFrameId != parent.Id || attack.SourceSeat != use.SourceSeat ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } action || action.ActorSeat != use.SourceSeat ||
            action.ProviderSeat != use.SourceSeat || action.PhysicalCards.Count != 0 ||
            action.RequesterSeat is not null || action.ResponderSeat is not null || action.OpponentSeat is not null ||
            action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 || parent.InstructionIndex < 1 ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats)) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != parent.Id ||
            _contentRegistry.GetSkill(parent.SkillId).Program is not { } program || program.GameplayHash != parent.GameplayHash)
            return false;
        var history = CompleteProgramEventHistory().ToArray();
        if (history.OfType<TargetsConfirmedEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not { TargetSeats.Count: 1 } declared ||
            history.OfType<CardUseDeclaredEvent>().SingleOrDefault(e => e.ResolutionId == use.Id) is not
                { CardId: 0, CardKind: CardKind.Slash } declaration || declaration.SourceSeat != action.ActorSeat ||
            history.OfType<CardActionAcceptedEvent>().Select(e => e.Action).FirstOrDefault(a => a.ActionId == action.ActionId) is not
                { Type: CardActionType.Use, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 0 } accepted ||
            accepted.ActorSeat != action.ActorSeat || accepted.ProviderSeat != action.ProviderSeat ||
            !accepted.TargetSeats.SequenceEqual(declared.TargetSeats) || !accepted.ConversionChain.SequenceEqual(action.ConversionChain)) return false;
        var original = declared.TargetSeats[0];
        var paused = ProgramInstructionResolver.Default.Resolve(parent, program)
            .GetPausedInstruction(parent.InstructionIndex).Effect;
        var exact = paused.Op switch
        {
            SkillProgramEffectOp.OfferVirtualSlashOrDraw => parent.SelectedTargetSeats.SequenceEqual([action.ActorSeat]) &&
                action.ConversionChain.Count == 0,
            SkillProgramEffectOp.UseVirtualSlash => parent.OwnerSeat == action.ActorSeat &&
                action.ConversionChain.SequenceEqual([new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)]) &&
                (paused.TargetReference is null ? parent.SelectedTargetSeats.SequenceEqual([original]) :
                    paused.TargetReference.Kind == ProgramParticipantRef.EventTarget && parent.WindowContext?.TargetSeat == original),
            SkillProgramEffectOp.UseVirtualCard or SkillProgramEffectOp.OfferUnlimitedVirtualSlash =>
                parent.OwnerSeat == action.ActorSeat && parent.SelectedTargetSeats.SequenceEqual([original]) &&
                action.ConversionChain.Count == 0 && paused.UseCardActionWindows && paused.OutputKind == CardKind.Slash &&
                paused.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget &&
                (parent.TriggerId is not null || paused.Op == SkillProgramEffectOp.OfferUnlimitedVirtualSlash),
            _ => false
        };
        if (!exact) return false;
        // Preserve the mature producer's one primary post-redirection return.
        primary = Array.AsReadOnly(new[] { use.TargetSeats[0] }); return true;
    }
    private void AssertForeignContestAidPrograms()
    {
        foreach (var root in _resolutionStack.OfType<ProgramSkillFrame>())
        { AssertForeignTurnContest(root); AssertSameTypeAid(root); }
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(u => u.ForeignTurnContestSlashReturn is not null))
        {
            var returned = use.ForeignTurnContestSlashReturn!;
            var index = _resolutionStack.FindIndex(f => f.Id == use.Id);
            if (index <= 0 || _resolutionStack[index - 1] is not ProgramSkillFrame root ||
                root.Id != returned.ProgramFrameId || root.ForeignTurnContest is not { Stage: ForeignTurnContestStage.SlashIssued } contest ||
                contest.SlashReturn != returned || returned.Origin != contest.Origin ||
                use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
                (use.CurrentSlashFirePolicy?.OriginalAction ?? use.Action) is not { Type: CardActionType.Use, EffectiveKind: CardKind.Slash } action ||
                action.ActionId != returned.CardActionId || action.ActorSeat != returned.Origin.Result.OpponentSeat ||
                action.ProviderSeat != action.ActorSeat || action.PhysicalCards.Count != 0 ||
                !CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Any(e => e.ResolutionId == use.Id &&
                    e.TargetSeats.SequenceEqual([returned.Origin.Result.SourceSeat])) ||
                use.TargetSeats.Count > 1 && !HasSameTypeAidTargetTail(use) && !HasIssuedOriginalTargetAdditionTail(use) && !IsCurrentSlashFireChangedUse(use))
                throw new InvalidOperationException("A foreign virtual Slash lost its exact issued zero-entity use and typed parent.");
        }
    }
}
