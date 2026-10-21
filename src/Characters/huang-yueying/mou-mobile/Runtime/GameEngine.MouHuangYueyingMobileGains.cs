namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 31500, SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains)]
    private bool CollectMouHyQiOnlyIssued(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger) => false;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31500, SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains)]
    private bool CanRunMouHyQiOnlyIssued(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        IsIssuedMouHyQiCandidate(candidate, context);
    private void CaptureMouHyQiGains(CardMovementBatchContext batch)
    {
        if (!HasMouHuangYueyingMobile) return;
        foreach (var grant in ProgramEventHistory<MouHuangYueyingQiGrantedEvent>().GroupBy(e => e.RecipientSeat).Select(g => g.Last()).ToArray())
        {
            if (!_players[grant.Source.OwnerSeat].IsAlive || !_players[grant.RecipientSeat].IsAlive) continue;
            var remaining = MouHyQiRemaining(grant);
            if (remaining == 0) continue;
            foreach (var pair in batch.Movements.Select((m, i) => (Movement: m, Index: i)).OrderBy(p => p.Movement.Sequence))
            {
                var m = pair.Movement;
                if (remaining == 0) break;
                if (m.Sequence <= grant.MovementSequence || m.To != CardLocation.Hand(grant.RecipientSeat) ||
                    ActualHandGainOriginalSource(m, batch) == CardLocation.Hand(grant.RecipientSeat) || !IsOrdinaryTrick(m.CardKind) ||
                    ProgramEventHistory<MouHuangYueyingQiGainClaimedEvent>().Any(e => e.GrantId == grant.GrantId && e.GainSequence == m.Sequence)) continue;
                var source = grant.Source with { BindingId = "receive-three" };
                AdvanceEventRulesAndQueueFact(new MouHuangYueyingQiGainClaimedEvent(grant.GrantId, batch.Id, pair.Index, source, grant.GameplayHash,
                    grant.RecipientSeat, m.CardId, ActualHandGainOriginalSource(m, batch), m.Sequence, --remaining));
            }
            SyncMouHyQiMarker(batch.Id, grant.RecipientSeat);
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectMouHyQiGainCandidates(CardMovementBatchContext batch) => !HasMouHuangYueyingMobile ? [] :
        ProgramEventHistory<MouHuangYueyingQiGainClaimedEvent>().Where(e => e.BatchId == batch.Id && _players[e.Source.OwnerSeat].IsAlive &&
            !ProgramEventHistory<MouHuangYueyingQiClaimSettledEvent>().Any(t => t.GrantId == e.GrantId && t.GainSequence == e.GainSequence))
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.GameplayHash, 0, e.MovementIndex)).ToArray();
    private MouHuangYueyingQiGainClaimedEvent? MouHyQiBatchClaim(ProgramTriggerCandidate candidate, CardMovementBatchContext batch)
    {
        if (candidate.SkillId != MouHuangYueyingMobileComposition.Qicai || candidate.BindingId != "receive-three" ||
            candidate.OccurrenceIndex < 0 || candidate.OccurrenceIndex >= batch.Movements.Count) return null;
        var claim = ProgramEventHistory<MouHuangYueyingQiGainClaimedEvent>().SingleOrDefault(e => e.BatchId == batch.Id && e.MovementIndex == candidate.OccurrenceIndex &&
            e.Source == new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId) && e.GameplayHash == candidate.GameplayHash);
        if (claim is null || batch.Movements[claim.MovementIndex] is not { } m || m.CardId != claim.CardId || m.Sequence != claim.GainSequence ||
            m.To != CardLocation.Hand(claim.GainingSeat) || ActualHandGainOriginalSource(m, batch) != claim.OriginalFrom || !IsOrdinaryTrick(m.CardKind)) return null;
        var grant = ProgramEventHistory<MouHuangYueyingQiGrantedEvent>().SingleOrDefault(g => g.GrantId == claim.GrantId);
        return grant is not null && grant.RecipientSeat == claim.GainingSeat && (grant.Source with { BindingId = "receive-three" }) == claim.Source &&
            grant.GameplayHash == claim.GameplayHash && claim.GainSequence > grant.MovementSequence ? claim : null;
    }
    private bool IsMouHyQiGainCandidate(ProgramTriggerCandidate candidate, CardMovementBatchContext batch) => MouHyQiBatchClaim(candidate, batch) is not null;
    private MouHuangYueyingQiGainClaimedEvent? MouHyQiClaim(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != candidate.OwnerSeat || context.TargetSeat != candidate.OwnerSeat ||
            context.MovementBatch is not { } batch || batch.Id != context.ParentFrameId || context.MovementIndex != candidate.OccurrenceIndex ||
            context.OccurrenceIndex != candidate.OccurrenceIndex ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(f => f.Id == batch.Id) is not { } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != candidate ||
            !parent.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        var parentIndex = _resolutionStack.FindIndex(f => f.Id == parent.Id);
        if (parentIndex != _resolutionStack.Count - 1 &&
            (_resolutionStack[parentIndex + 1] is not ProgramSkillFrame child || child.OwnerSeat != candidate.OwnerSeat || child.SkillId != candidate.SkillId ||
             child.TriggerId != candidate.BindingId || child.SkillInstanceId != candidate.SkillInstanceId || child.GameplayHash != candidate.GameplayHash || child.WindowContext != context)) return null;
        var claim = MouHyQiBatchClaim(candidate, batch);
        return claim is not null && context.SourceSeat == claim.GainingSeat ? claim : null;
    }
    private ProgramSkillWindowContext CreateMouHyQiGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate c)
    {
        var claim = MouHyQiBatchClaim(c, parent.Batch) ?? throw new InvalidOperationException("Qicai gain context requires its complete original batch claim.");
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, c.OwnerSeat, SourceSeat: claim.GainingSeat, TargetSeat: c.OwnerSeat,
            OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: parent.Batch, MovementIndex: c.OccurrenceIndex);
    }
    private bool IsIssuedMouHyQiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => _players[c.OwnerSeat].IsAlive && MouHyQiClaim(c, context) is { } claim &&
        !ProgramEventHistory<MouHuangYueyingQiClaimSettledEvent>().Any(e => e.GrantId == claim.GrantId && e.GainSequence == claim.GainSequence);
    private SkillProgramStepOutcome BeginMouHyQiTransfer(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouHuangYueyingMobile is not null) { ContinueMouHy(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.WindowContext is not { } context || !IsIssuedMouHyQiCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex), context))
            throw new InvalidOperationException("Qicai transfer requires its exact original accepted actual gain claim.");
        var claim = MouHyQiClaim(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0, context.OccurrenceIndex), context)!;
        var before = _movementSequence; var from = CardLocation.Hand(claim.GainingSeat);
        ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = new(MouHySource(f), f.GameplayHash, SkillProgramEffectOp.TransferMouHuangYueyingQicaiGains,
            MouHuangYueyingMobileStage.MovementChildren, _turnNumber, _turnProgression.OwnerSeat, context.ParentFrameId, claim.GainingSeat, Paid: true,
            CardId: claim.CardId, From: from, Before: before, After: before, GrantId: claim.GrantId, ClaimSequence: claim.GainSequence), PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (_players[f.OwnerSeat].IsAlive && _players[claim.GainingSeat].IsAlive && _cardZones.GetLocation(claim.CardId) == from && _winner == Winner.None)
        {
            MoveCard(GetAdvancedCard(claim.CardId), from, CardLocation.Hand(f.OwnerSeat), new("skill-program.mou-mobile:qicai.transfer"), NativeGiveCompletion(f.Id,claim.GainingSeat,f.OwnerSeat,[claim.CardId],before,m =>
                AdvanceEventRulesAndQueueFact(new MouHuangYueyingQiTransferredEvent(f.Id, claim.GrantId, claim.GainSequence, claim.CardId, m.From, m.To, m.Sequence))), disclosure: CardMovementDisclosure.FaceDown);
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouHuangYueyingMobile = f.MouHuangYueyingMobile! with { After = _movementSequence } });
        ContinueMouHy(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
}
