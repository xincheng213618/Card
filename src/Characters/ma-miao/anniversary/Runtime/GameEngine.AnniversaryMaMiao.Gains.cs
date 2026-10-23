namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool IsAnniversaryMaMiaoCongfengTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng }];
    private bool HasAnniversaryMaMiaoGains => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryMaMiaoCongfeng);
    private void CaptureMaMiaoTransferProducer(CardMovementBatchContext batch)
    {
        foreach (var entry in batch.Movements.Where(m => m.From is { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: not null } && m.To == CardLocation.Processing))
        {
            var holder = entry.From.OwnerSeat!.Value;
            if (batch.ParentFrameId is { } useId && LifecycleCardUse(useId) is { CardKind: CardKind.Snatch, Action: { } action } use &&
                action.EffectiveKind == CardKind.Snatch && action.ActorSeat == use.SourceSeat && action.ActorSeat != holder &&
                action.EffectiveDesignatedTargetSeats.Contains(holder) && entry.Reason == CardMoveReasons.Snatch &&
                ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == useId && e.SourceSeat == action.ActorSeat && e.CardKind == CardKind.Snatch))
                Record(action.ActorSeat, useId, true, null, null);
            else if (_resolutionStack.LastOrDefault() is ProgramSkillFrame p && batch.ParentFrameId == p.Id &&
                batch.OriginOwnerSeat == p.OwnerSeat && batch.OriginSkillId == p.SkillId && batch.OriginSkillInstanceId == p.SkillInstanceId &&
                _contentRegistry.GetSkill(p.SkillId).Program?.GameplayHash == p.GameplayHash &&
                GetPausedPrivateOfferEffect(p) is { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Destination: SkillProgramCardDestination.SelectedTargetHand,
                    CardOwnerRef: { } owner, TargetReference: { } recipientRef } effect && effect.Zones.Contains(entry.From.Zone) &&
                ResolveProgramParticipant(p, owner) == holder && entry.Reason.Value == $"skill-program.{p.SkillId}.{effect.Op}")
                Record(ResolveProgramParticipant(p, recipientRef), p.Id, false, new(p.SkillId, GetProgramBindingId(p), p.OwnerSeat, p.SkillInstanceId), p.GameplayHash);

            void Record(int recipient, long producer, bool snatch, CardConversionSource? source, string? hash)
            {
                if (recipient != holder && IsValidPlayerSeat(recipient) &&
                    !ProgramEventHistory<AnniversaryMaMiaoTransferProducerEvent>().Any(e => e.EntrySequence == entry.Sequence))
                    AdvanceEventRulesAndQueueFact(new AnniversaryMaMiaoTransferProducerEvent(entry.Sequence, recipient, producer, snatch, source, hash));
            }
        }
    }
    private CardLocation MaMiaoGainOriginalSource(CardMovementRecord movement, CardMovementBatchContext batch)
    {
        if (movement.From != CardLocation.Processing || movement.To is not { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient }) return movement.From;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == movement.CardId && m.Sequence < movement.Sequence);
        if (entry is not { From.Zone: CardZoneKind.Hand or CardZoneKind.Equipment, From.OwnerSeat: { } holder } || entry.To != CardLocation.Processing ||
            holder == recipient || entry.TurnNumber != batch.TurnNumber ||
            ProgramEventHistory<AnniversaryMaMiaoTransferProducerEvent>().SingleOrDefault(e => e.EntrySequence == entry.Sequence &&
                e.Recipient == recipient && e.ProducerId == batch.ParentFrameId) is not { } proof) return movement.From;
        // Only an issued owned-card transfer can cross Processing. A used card's
        // material has no owner and cannot inherit its former Hand owner here.
        if (proof.NativeSnatch)
            return proof.Source is null && proof.Hash is null && entry.Reason == CardMoveReasons.Snatch && movement.Reason == CardMoveReasons.SnatchFinished &&
                ProgramEventHistory<TargetCardTakenEvent>().Any(e => e.ResolutionId == proof.ProducerId && e.SourceSeat == recipient && e.TargetSeat == holder &&
                    e.FromZone == entry.From.Zone && (e.PublicCardId is null || e.PublicCardId == movement.CardId)) ? entry.From : movement.From;
        return proof.Source is { } source && proof.Hash == _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash &&
            batch.OriginSkillId == source.SkillId && batch.OriginSkillInstanceId == source.SkillInstanceId && batch.OriginOwnerSeat == source.OwnerSeat &&
            entry.Reason == movement.Reason ? entry.From : movement.From;
    }
    private IEnumerable<(int Index, CardMovementRecord Move, int Recipient, int Holder)> MaMiaoForeignGains(CardMovementBatchContext batch, int owner)
    {
        if (batch.OriginSkillId == AnniversaryMaMiaoComposition.Congfeng) yield break;
        foreach (var item in batch.Movements.Select((m, i) => (m, i, From: MaMiaoGainOriginalSource(m, batch))).Where(x =>
            x.m.To is { Zone: CardZoneKind.Hand, OwnerSeat: { } receiver } && x.From is { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: { } holder } &&
            receiver != holder && (receiver == owner || holder == owner)).GroupBy(x => (x.m.To.OwnerSeat, x.From.OwnerSeat)).Select(g => g.OrderBy(x => x.m.Sequence).First()))
            yield return (item.i, item.m, item.m.To.OwnerSeat!.Value, item.From.OwnerSeat!.Value);
    }
    private void CaptureAnniversaryMaMiaoGainBatch(CardMovementBatchContext batch)
    {
        if (!HasAnniversaryMaMiaoGains) return;
        CaptureMaMiaoTransferProducer(batch);
        foreach (var owner in _players.Where(p => p.IsAlive))
        foreach (var c in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained).Where(c => IsAnniversaryMaMiaoCongfengTrigger(GetProgramTrigger(c))))
        foreach (var gain in MaMiaoForeignGains(batch, owner.Seat))
            AdvanceEventRulesAndQueueFact(new AnniversaryMaMiaoGainQualifiedEvent(batch.Id, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash, gain.Index, gain.Recipient, gain.Holder, gain.Move.Sequence));
    }
    private IEnumerable<ProgramTriggerCandidate> CollectAnniversaryMaMiaoGainCandidates(CardMovementBatchContext batch)
    {
        if (!HasAnniversaryMaMiaoGains) return [];
        return ProgramEventHistory<AnniversaryMaMiaoGainQualifiedEvent>().Where(e => e.BatchId == batch.Id && _players[e.Source.OwnerSeat].IsAlive &&
            HasRuntimeSkillInstance(_players[e.Source.OwnerSeat], e.Source.SkillId, e.Source.SkillInstanceId) && _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.Hash)
            .Select(e => new ProgramTriggerCandidate(e.Source.OwnerSeat, e.Source.SkillId, e.Source.BindingId, e.Source.SkillInstanceId, e.Hash,
                _contentRegistry.GetSkill(e.Source.SkillId).Program!.Triggers.Single(t => t.Id == e.Source.BindingId).Priority, e.MovementIndex)).ToArray();
    }
    private ProgramSkillWindowContext CreateAnniversaryMaMiaoGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate c)
    {
        var e = ProgramEventHistory<AnniversaryMaMiaoGainQualifiedEvent>().Single(e => e.BatchId == parent.Batch.Id && e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && e.MovementIndex == c.OccurrenceIndex);
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, c.OwnerSeat, SourceSeat: e.Recipient, TargetSeat: c.OwnerSeat,
            OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: parent.Batch, MovementIndex: c.OccurrenceIndex);
    }
    private AnniversaryMaMiaoGainQualifiedEvent? MaMiaoGainQualification(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
    {
        if (w.Window != SkillProgramTriggerWindow.CardsGained || w.OwnerSeat != c.OwnerSeat || w.MovementBatch is not { } b ||
            w.MovementIndex != c.OccurrenceIndex || w.OccurrenceIndex != c.OccurrenceIndex || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(p => p.Id == w.ParentFrameId) is not { } parent ||
            parent.Batch.Id != b.Id || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c || !parent.Batch.Movements.SequenceEqual(b.Movements)) return null;
        var e = ProgramEventHistory<AnniversaryMaMiaoGainQualifiedEvent>().SingleOrDefault(e => e.BatchId == b.Id && e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) &&
            e.Hash == c.GameplayHash && e.MovementIndex == c.OccurrenceIndex);
        return e is not null && w.SourceSeat == e.Recipient && w.TargetSeat == c.OwnerSeat && MaMiaoForeignGains(b, c.OwnerSeat).Any(g => g.Index == e.MovementIndex && g.Move.Sequence == e.Sequence && g.Recipient == e.Recipient && g.Holder == e.Holder) ? e : null;
    }
}
