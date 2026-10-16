namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasQingyuanCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SelectQingyuanOpeningPartner);
    private bool HasZhongshenCapability => _contentRegistry.ProgramDependencies.UsesRoundGainedRedHandOnly;
    private static CardConversionSource HuJinDingSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private IEnumerable<QingyuanPartnerSelectedEvent> QingyuanPartners(int owner, string skill, string instance) => ProgramEventHistory<QingyuanPartnerSelectedEvent>()
        .Where(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Source.SkillInstanceId == instance);
    private bool IsLiveQingyuanPartner(QingyuanPartnerSelectedEvent p) => _players[p.PartnerSeat].IsAlive && _players[p.Source.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[p.Source.OwnerSeat], p.Source.SkillId, p.Source.SkillInstanceId) && _contentRegistry.GetSkill(p.Source.SkillId).Program?.GameplayHash == p.GameplayHash;
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 26820)]
    private void ObserveHuJinDingFacts(IGameEvent fact)
    {
        if (fact is RoundStartedEvent round && HasZhongshenCapability)
        {
            if (ProgramEventHistory<ZhongshenRoundBoundaryEvent>().Any(e => e.RoundNumber == round.RoundNumber)) throw new InvalidOperationException("Zhongshen cannot enter the same actual round twice.");
            AdvanceEventRulesAndQueueFact(new ZhongshenRoundBoundaryEvent(round.RoundNumber, _movementSequence));
        }
        if (!HasQingyuanCapability || fact is not DamageAppliedEvent { Amount: > 0 } applied ||
            ProgramEventHistory<DamageAppliedEvent>().Count(e => e.TargetSeat == applied.TargetSeat) != 1 || CurrentDamageAttempt is not { DamageWasApplied: true } attempt) return;
        var damage = _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.ParentFrameId == attempt.ResolutionId && d.TargetSeat == applied.TargetSeat && d.Amount == applied.Amount)
            ?? throw new InvalidOperationException("Qingyuan lost its exact first actual damage frame.");
        AdvanceEventRulesAndQueueFact(new QingyuanFirstDamageEvent(applied.TargetSeat, damage.Id, damage.ParentFrameId, applied.Amount));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 26820)]
    private void SynchronizeQingyuanPublicMarkers()
    {
        if (!HasQingyuanCapability) return;
        var valid = ProgramEventHistory<QingyuanPartnerSelectedEvent>().Where(IsLiveQingyuanPartner).ToArray();
        foreach (var player in _players)
        {
            var marker = PlayerMarkerKind.QingyuanPartner; var previous = player.Markers.GetValueOrDefault(marker);
            foreach (var key in player.MarkerSourceCounts.Keys.Where(k => k.Marker == marker).ToArray()) player.MarkerSourceCounts.Remove(key);
            var sources = valid.Where(p => p.PartnerSeat == player.Seat).GroupBy(p => p.Source.OwnerSeat).ToArray();
            foreach (var source in sources) player.MarkerSourceCounts[(marker, source.Key)] = source.Count();
            var count = sources.Sum(s => s.Count());
            if (count == 0) player.Markers.Remove(marker); else player.Markers[marker] = count;
            if (previous != count) AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(0, player.Seat, marker, count - previous, count, null, "skill-program.qingyuan.marker"));
        }
    }
    private static bool IsQingyuanGainTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.TakeRandomQingyuanHandAfterGain }];
    private void CaptureQingyuanGainBatch(CardMovementBatchContext batch)
    {
        if (!HasQingyuanCapability || batch.TurnNumber < 1) return;
        foreach (var owner in _players.Where(p => p.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained).Where(c => IsQingyuanGainTrigger(GetProgramTrigger(c))))
        {
            var partners = QingyuanPartners(owner.Seat, candidate.SkillId, candidate.SkillInstanceId).Where(IsLiveQingyuanPartner).ToArray();
            var gain = batch.Movements.Select((m, i) => (m, i)).FirstOrDefault(pair => pair.m.To is { Zone: CardZoneKind.Hand, OwnerSeat: { } seat } &&
                partners.Any(p => p.PartnerSeat == seat && p.MovementSequence < pair.m.Sequence) && ActualHandGainOriginalSource(pair.m, batch) != CardLocation.Hand(seat));
            if (gain.m is null) continue;
            AdvanceEventRulesAndQueueFact(new QingyuanGainQualifiedEvent(batch.Id, new(candidate.SkillId, candidate.BindingId, owner.Seat, candidate.SkillInstanceId), candidate.GameplayHash,
                batch.TurnNumber, gain.i, gain.m.To.OwnerSeat!.Value, gain.m.Sequence));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectQingyuanGainCandidates(CardMovementBatchContext batch)
    {
        if (!HasQingyuanCapability) return [];
        return ProgramEventHistory<QingyuanGainQualifiedEvent>().Where(q => q.BatchId == batch.Id && q.ActualTurn == _turnNumber && _players[q.Source.OwnerSeat].IsAlive &&
                HasRuntimeSkillInstance(_players[q.Source.OwnerSeat], q.Source.SkillId, q.Source.SkillInstanceId) && _contentRegistry.GetSkill(q.Source.SkillId).Program?.GameplayHash == q.GameplayHash)
            .Select(q => new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId, q.Source.SkillInstanceId, q.GameplayHash,
                _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId).Priority, q.MovementIndex)).ToArray();
    }
    private ProgramSkillWindowContext CreateQingyuanGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate c)
    {
        var q = ProgramEventHistory<QingyuanGainQualifiedEvent>().Single(q => q.BatchId == parent.Id && q.Source.OwnerSeat == c.OwnerSeat &&
            q.Source.SkillId == c.SkillId && q.Source.SkillInstanceId == c.SkillInstanceId && q.MovementIndex == c.OccurrenceIndex);
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, c.OwnerSeat, SourceSeat: q.GainingSeat, TargetSeat: c.OwnerSeat,
            OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: parent.Batch, MovementIndex: q.MovementIndex);
    }
    private QingyuanGainQualifiedEvent? QingyuanGainQualification(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat ||
            context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || context.MovementIndex != c.OccurrenceIndex || context.OccurrenceIndex != c.OccurrenceIndex ||
            c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c || !parent.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        var q = ProgramEventHistory<QingyuanGainQualifiedEvent>().SingleOrDefault(q => q.BatchId == batch.Id && q.ActualTurn == _turnNumber && q.Source.OwnerSeat == c.OwnerSeat &&
            q.Source.SkillId == c.SkillId && q.Source.SkillInstanceId == c.SkillInstanceId && q.Source.BindingId == c.BindingId && q.GameplayHash == c.GameplayHash && q.MovementIndex == c.OccurrenceIndex);
        return q is not null && context.SourceSeat == q.GainingSeat && batch.Movements[c.OccurrenceIndex] is { } move && move.Sequence == q.GainSequence &&
            move.To == CardLocation.Hand(q.GainingSeat) && ActualHandGainOriginalSource(move, batch) != CardLocation.Hand(q.GainingSeat) &&
            QingyuanPartners(c.OwnerSeat, c.SkillId, c.SkillInstanceId).Any(p => p.PartnerSeat == q.GainingSeat && p.MovementSequence < q.GainSequence) ? q : null;
    }
    private bool MatchesZhongshenRoundGain(CharacterState owner, Card card, SkillProgramViewAs rule)
    {
        if (rule.RoundGainedRedHandOnly != true) return true;
        if (!owner.IsAlive || card.IsGeneralWeapon || _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat) || SuitColor(EffectiveSuit(owner, card)) != true ||
            ProgramEventHistory<ZhongshenRoundBoundaryEvent>().SingleOrDefault(e => e.RoundNumber == _roundNumber) is not { } round) return false;
        return _cardMovements.Any(m => m.CardId == card.Id && m.Sequence > round.MovementSequence && m.To == CardLocation.Hand(owner.Seat) && ActualHandGainOriginalSource(m) != CardLocation.Hand(owner.Seat));
    }
    private bool HasZhongshenCardMark(int cardId)
    {
        if (_cardZones.GetLocation(cardId) is not { Zone: CardZoneKind.Hand, OwnerSeat: { } owner } || !IsValidPlayerSeat(owner)) return false;
        var card = GetAdvancedCard(cardId);
        return EnabledViewAsPrograms(_players[owner]).SelectMany(p => p.ViewAs).Any(r => r.RoundGainedRedHandOnly == true && MatchesZhongshenRoundGain(_players[owner], card, r));
    }
}
