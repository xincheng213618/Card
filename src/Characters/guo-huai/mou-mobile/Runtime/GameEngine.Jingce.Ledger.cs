namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMouGuoHuaiMobile => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart);
    private static CardConversionSource MouGhSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guo_huai_mou_mobile_Jingce_Ledger.MouGhSource(f);
    private static bool IsMouGuoHuaiGainTrigger(SkillProgramTrigger t) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guo_huai_mou_mobile_Jingce_Ledger.IsMouGuoHuaiGainTrigger(t);
    private bool LiveMouGhSource(CardConversionSource source, string hash) => _players[source.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[source.OwnerSeat], source.SkillId, source.SkillInstanceId) && _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash == hash;
    private IEnumerable<MouGuoHuaiPredictionIssuedEvent> UnverifiedMouGhPredictions() => ProgramEventHistory<MouGuoHuaiPredictionIssuedEvent>().Where(p =>
        ProgramEventHistory<MouGuoHuaiPredictionPlacedEvent>().Any(e => e.PredictionId == p.PredictionId && e.CardId == p.CardId && e.To == CardLocation.DrawPile) &&
        !ProgramEventHistory<MouGuoHuaiPredictionVerifiedEvent>().Any(e => e.PredictionId == p.PredictionId) && !ProgramEventHistory<MouGuoHuaiPredictionDueEvent>().Any(e => e.PredictionId == p.PredictionId) && !ProgramEventHistory<MouGuoHuaiPredictionConsumedEvent>().Any(e => e.PredictionId == p.PredictionId));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 32000)]
    private void FreezeMouGhActualStart(TurnStartedEvent turn)
    {
        if (!HasMouGuoHuaiMobile) return;
        foreach (var p in UnverifiedMouGhPredictions().Where(p => p.Source.OwnerSeat == turn.ActorSeat && p.ActualTurn < turn.TurnNumber).ToArray())
            AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionDueEvent(p.PredictionId, turn.TurnNumber, turn.ActorSeat, p.RecipientSeat is null));
    }
    private bool TryBeginMouGuoHuaiActualStart(CharacterState owner)
    {
        if (!HasMouGuoHuaiMobile || _winner != Winner.None || !owner.IsAlive || ProgramEventHistory<MouGuoHuaiStartedEvent>().Any(e => e.Op == SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart && e.Source.OwnerSeat == owner.Seat && e.ActualTurn == _turnNumber)) return false;
        var facts = CaptureProgramTriggerFacts(owner);
        var candidates = CollectEligibleProgramTriggerCandidates(owner, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, facts).Where(c => GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart }]).ToArray();
        if (candidates.Length == 0) return false;
        if (_resolutionStack.Count != 0 || _pendingDecision is not null || owner.Seat != _currentSeat) throw new InvalidOperationException("Jingce actual-start boundary must retain its clean native turn cursor.");
        var frame = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, owner.Seat, SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, candidates, ProgramLifecycleContinuation.MouGuoHuaiActualStart, facts);
        PushRuntimeFrame(frame); AdvanceRuntimeTop<ProgramLifecycleTriggerWindowFrame>(); return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32000, SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart, SkillProgramEffectOp.ResolveMouGuoHuaiJingcePrediction, SkillProgramEffectOp.ResolveMouGuoHuaiJingceReward)]
    private bool CanOfferMouGh(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (IsMouGuoHuaiGainTrigger(t)) return MouGuoHuaiGainQualification(c, context) is not null;
        if (t.Effects is [{ Op: SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart }]) return context.SourceSeat == c.OwnerSeat && !ProgramEventHistory<MouGuoHuaiStartedEvent>().Any(e => e.Op == SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart && e.Source.OwnerSeat == c.OwnerSeat && e.ActualTurn == _turnNumber);
        return context.Window == SkillProgramTriggerWindow.AfterTurnEnded && context.SourceSeat == c.OwnerSeat && PublicPileSources(c.OwnerSeat).Any(s => s.SkillId == c.SkillId && s.SkillInstanceId == c.SkillInstanceId && PublicPileCards(s).Count != 0);
    }
    // This hook runs at completion of the genuine atomic movement batch, before
    // any gain observer can give the same original entity to a second player.
    private void CaptureMouGuoHuaiGainBatch(CardMovementBatchContext batch)
    {
        if (!HasMouGuoHuaiMobile) return;
        foreach (var p in UnverifiedMouGhPredictions().ToArray())
        {
            var placed = ProgramEventHistory<MouGuoHuaiPredictionPlacedEvent>().Single(e => e.PredictionId == p.PredictionId);
            var gain = batch.Movements.Select((m, i) => (m, i)).FirstOrDefault(x => x.m.CardId == p.CardId && x.m.Sequence > placed.Sequence && x.m.To is { Zone: CardZoneKind.Hand, OwnerSeat: not null } && ActualHandGainOriginalSource(x.m, batch) != x.m.To);
            if (gain.m?.To.OwnerSeat is not { } recipient) continue;
            var success = p.RecipientSeat == recipient;
            AdvanceEventRulesAndQueueFact(new MouGuoHuaiPredictionVerifiedEvent(p.PredictionId, batch.Id, gain.i, gain.m.Sequence, recipient, success));
            if (!success || !LiveMouGhSource(p.Source, p.GameplayHash)) continue;
            foreach (var c in CollectProgramTriggerCandidates(_players[p.Source.OwnerSeat], SkillProgramTriggerWindow.CardsGained).Where(c => IsMouGuoHuaiGainTrigger(GetProgramTrigger(c)) && c.SkillInstanceId == p.Source.SkillInstanceId))
                AdvanceEventRulesAndQueueFact(new MouGuoHuaiGainQualifiedEvent(p.PredictionId, batch.Id, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash, gain.i, gain.m.Sequence, recipient));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectMouGuoHuaiGainCandidates(CardMovementBatchContext batch) => ProgramEventHistory<MouGuoHuaiGainQualifiedEvent>().Where(q => q.BatchId == batch.Id && LiveMouGhSource(q.Source, q.GameplayHash)).Select(q => new ProgramTriggerCandidate(q.Source.OwnerSeat, q.Source.SkillId, q.Source.BindingId, q.Source.SkillInstanceId, q.GameplayHash, _contentRegistry.GetSkill(q.Source.SkillId).Program!.Triggers.Single(t => t.Id == q.Source.BindingId).Priority, q.MovementIndex)).ToArray();
    private MouGuoHuaiGainQualifiedEvent? MouGuoHuaiGainQualification(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.CardsGained || context.OwnerSeat != c.OwnerSeat || context.TargetSeat != c.OwnerSeat || context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || context.MovementIndex != c.OccurrenceIndex || context.OccurrenceIndex != c.OccurrenceIndex || c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c || !parent.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        var q = ProgramEventHistory<MouGuoHuaiGainQualifiedEvent>().SingleOrDefault(q => q.BatchId == batch.Id && q.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && q.GameplayHash == c.GameplayHash && q.MovementIndex == c.OccurrenceIndex);
        return q is not null && context.SourceSeat == q.GainingSeat && batch.Movements[q.MovementIndex] is { } move && move.CardId == ProgramEventHistory<MouGuoHuaiPredictionIssuedEvent>().Single(p => p.PredictionId == q.PredictionId).CardId && move.Sequence == q.Sequence && move.To == CardLocation.Hand(q.GainingSeat) && ActualHandGainOriginalSource(move, batch) != move.To && ProgramEventHistory<MouGuoHuaiPredictionVerifiedEvent>().Count(e => e.PredictionId == q.PredictionId && e.Success && e.BatchId == batch.Id && e.Sequence == q.Sequence && e.GainingSeat == q.GainingSeat) == 1 ? q : null;
    }
    private ProgramSkillWindowContext CreateMouGuoHuaiGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate c)
    {
        var q = ProgramEventHistory<MouGuoHuaiGainQualifiedEvent>().Single(q => q.BatchId == parent.Id && q.Source.OwnerSeat == c.OwnerSeat && q.Source.SkillId == c.SkillId && q.Source.SkillInstanceId == c.SkillInstanceId && q.MovementIndex == c.OccurrenceIndex);
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, c.OwnerSeat, SourceSeat: q.GainingSeat, TargetSeat: c.OwnerSeat, OccurrenceIndex: c.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: parent.Batch, MovementIndex: q.MovementIndex);
    }
}
