namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasXieLingYuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYuandiDiscardNames) || _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveXinyouBasicCycle);
    private static CardConversionSource XieLingYuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static int XieLingYuNameBit(CardKind kind) => ProgramBasicCardName(kind) switch { CardKind.Slash => 1, CardKind.Dodge => 2, CardKind.Peach => 4, CardKind.Alcohol => 8, _ => 0 };
    private int YuandiRemainingMask(int owner) => ProgramEventHistory<YuandiNameRemovedEvent>().Where(e => e.OwnerSeat == owner).Aggregate(7, (mask, e) => mask & ~XieLingYuNameBit(e.Name));
    private static CardKind[] XieLingYuNames(int mask) => new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol }.Where(k => (mask & XieLingYuNameBit(k)) != 0).ToArray();
    private void CaptureXieLingYuMovementBatch(CardMovementBatchContext batch)
    {
        if (!HasXieLingYuCapability || batch.TurnNumber <= 0) return;
        foreach (var m in batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To && XieLingYuNameBit(m.CardKind) is > 0 and < 8))
        {
            if (ProgramEventHistory<YuandiDiscardEnteredEvent>().Any(e => e.MovementSequence == m.Sequence)) continue;
            AdvanceEventRulesAndQueueFact(new YuandiDiscardEnteredEvent(batch.Id, batch.TurnNumber, _turnProgression.OwnerSeat, m.Sequence,
                m.CardId, m.CardKind, ProgramBasicCardName(m.CardKind), IsNativeTrueUseDiscardMovement(batch, m)));
        }
    }
    private (long Sequence, bool NonUse)? YuandiFirstGroup(int owner, int turn, int turnOwner, int mask)
    {
        var found = 0; var nonUse = false;
        foreach (var e in ProgramEventHistory<YuandiDiscardEnteredEvent>().Where(e => e.ActualTurn == turn && e.TurnOwner == turnOwner).OrderBy(e => e.MovementSequence))
        {
            var bit = XieLingYuNameBit(e.Name); if ((bit & mask) == 0) continue;
            found |= bit; nonUse |= !e.FromTrueUse;
            if ((found & mask) == mask) return (e.MovementSequence, nonUse);
        }
        return null;
    }
    private IReadOnlyList<ProgramTriggerCandidate> CollectYuandiCandidates(CardMovementBatchContext batch)
    {
        if (!HasXieLingYuCapability || batch.TurnNumber != _turnNumber) return [];
        var result = new List<ProgramTriggerCandidate>();
        foreach (var owner in _players.Where(p => p.IsAlive))
        {
            if (ProgramEventHistory<YuandiPaidEvent>().Any(e => e.OwnerSeat == owner.Seat && e.ActualTurn == batch.TurnNumber && e.TurnOwner == _turnProgression.OwnerSeat)) continue;
            var binding = CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved).Where(c =>
                GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.ResolveYuandiDiscardNames }]).OrderBy(c => c.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
            if (binding is null) continue;
            var mask = YuandiRemainingMask(owner.Seat);
            if (YuandiFirstGroup(owner.Seat, batch.TurnNumber, _turnProgression.OwnerSeat, mask) is not { } first) continue;
            var index = batch.Movements.ToList().FindIndex(m => m.Sequence == first.Sequence);
            if (index < 0) continue;
            var candidate = binding with { OccurrenceIndex = index };
            var fact = new YuandiOpportunityEvent(batch.Id, owner.Seat, candidate.SkillInstanceId, candidate.GameplayHash,
                batch.TurnNumber, _turnProgression.OwnerSeat, first.Sequence, mask, first.NonUse, index);
            if (!ProgramEventHistory<YuandiOpportunityEvent>().Contains(fact)) AdvanceEventRulesAndQueueFact(fact);
            result.Add(candidate);
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private YuandiOpportunityEvent? CurrentYuandiOpportunity(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (!HasXieLingYuCapability || c.SkillId != "ol:yuandi" || c.BindingId != "discard-name-group" || context.Window != SkillProgramTriggerWindow.CardsMoved || context.OwnerSeat != c.OwnerSeat || context.MovementBatch is not { } batch || context.ParentFrameId != batch.Id || context.OccurrenceIndex != c.OccurrenceIndex ||
            c.OccurrenceIndex < 0 || c.OccurrenceIndex >= batch.Movements.Count ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w => w.Id == batch.Id) is not { } parent || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || parent.Candidates[parent.CandidateIndex] != c || !parent.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        return ProgramEventHistory<YuandiOpportunityEvent>().SingleOrDefault(e => e.BatchId == batch.Id && e.OwnerSeat == c.OwnerSeat && e.SkillInstanceId == c.SkillInstanceId && e.GameplayHash == c.GameplayHash && e.OccurrenceIndex == c.OccurrenceIndex && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat && batch.Movements[c.OccurrenceIndex].Sequence == e.CompletionSequence);
    }
    private ProgramSkillWindowContext CreateYuandiMovementContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate candidate)
    {
        if (GetProgramTrigger(candidate).Effects is not [{ Op: SkillProgramEffectOp.ResolveYuandiDiscardNames }] || candidate.OccurrenceIndex < 0 || candidate.OccurrenceIndex >= frame.Batch.Movements.Count || !ProgramEventHistory<YuandiOpportunityEvent>().Any(e => e.BatchId == frame.Id && e.OwnerSeat == candidate.OwnerSeat && e.SkillInstanceId == candidate.SkillInstanceId && e.GameplayHash == candidate.GameplayHash && e.OccurrenceIndex == candidate.OccurrenceIndex && e.CompletionSequence == frame.Batch.Movements[candidate.OccurrenceIndex].Sequence)) throw new InvalidOperationException("Yuandi requires its original whole discard batch and issued name-group occurrence.");
        return new(SkillProgramTriggerWindow.CardsMoved, frame.Id, candidate.OwnerSeat, OccurrenceIndex: candidate.OccurrenceIndex, Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]), MovementBatch: frame.Batch, MovementIndex: candidate.OccurrenceIndex);
    }
    private bool IsIssuedYuandiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) => CurrentYuandiOpportunity(c, context) is { } e &&
        (_resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.XieLingYu is { Paid: true } r && r.Op == SkillProgramEffectOp.ResolveYuandiDiscardNames && r.OpportunitySequence == e.CompletionSequence && f.OwnerSeat == c.OwnerSeat && f.WindowContext == context) ||
         _players[c.OwnerSeat].IsAlive && !ProgramEventHistory<YuandiPaidEvent>().Any(p => p.OwnerSeat == c.OwnerSeat && p.ActualTurn == e.ActualTurn && p.TurnOwner == e.TurnOwner));
    private ActualDiscardRecoveryPhaseKey? XinyouActualPlay(int owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner && p.TurnNumber == _turnNumber && IsRecordedActualDiscardRecoveryPhase(p) ? p : null;
    private bool XinyouPhaseSpent(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<XinyouPaidEvent>().Any(e => e.OwnerSeat == owner && e.PhaseToken == phase.Token);
    private IReadOnlyList<Card> XinyouCostCards(int owner) => GetSelfDiscardableOwnedHand(_players[owner]).Where(c => GetProgramCardCategory(AdvancedEffectiveHandKind(_players[owner], c)) == SkillProgramCardCategory.Basic).ToArray();
    private bool IsXinyouHandLimitExempt(int ownerSeat, int cardId) => HasXieLingYuCapability && ProgramEventHistory<XinyouGainedEntityEvent>().Any(e => e.OwnerSeat == ownerSeat && e.CardId == cardId && e.ActualTurn == _turnNumber && e.TurnOwner == _turnProgression.OwnerSeat) && _cardZones.GetLocation(cardId) == CardLocation.Hand(ownerSeat);
    private bool HasXinyouCardMark(int ownerSeat, int cardId) => IsXinyouHandLimitExempt(ownerSeat, cardId);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 27582, SkillProgramEffectOp.ResolveXinyouBasicCycle)]
    private bool CanActivateXinyou(ProgramActivationLegalityContext c) => c.Owner.IsAlive && _phase == TurnPhase.Play && c.OwnerSeat == _currentSeat && XinyouActualPlay(c.OwnerSeat) is { } phase && !XinyouPhaseSpent(c.OwnerSeat, phase) && XinyouCostCards(c.OwnerSeat).Count > 0;
}
