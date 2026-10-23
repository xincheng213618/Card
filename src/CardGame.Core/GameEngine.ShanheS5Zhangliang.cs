namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsOwnTurnEnemyHandGainTrigger(SkillProgramTrigger trigger) =>
        trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.BindOwnTurnEnemyHandGain);

    private int[] OwnTurnEnemyHandGainIndexes(CardMovementBatchContext batch, int target)
    {
        var indexes = MatchingActualHandGainIndexes(batch, target, CardLocation.Hand(target));
        if (batch.NativeDrawInvocation is { } draw)
        {
            if (draw.OwnerSeat != target || draw.LastBatchId != batch.Id || draw.ParentFrameId != batch.ParentFrameId ||
                draw.ActualTurnNumber != batch.TurnNumber || !ValidNativeDrawInvocation(draw)) return [];
            var last = draw.Materials[^1];
            return indexes.Where(i => batch.Movements[i].Sequence == last.MovementSequence &&
                batch.Movements[i].CardId == last.CardId).ToArray();
        }
        // DrawCards moves physical entities separately. Its existing complete
        // invoice belongs to the last batch; earlier materials cannot issue
        // additional gain occurrences for that same logical draw.
        var draws = CompleteProgramEventHistory().OfType<NativeDrawInvocationRecordedEvent>()
            .Where(e => e.OwnerSeat == target).ToArray();
        return indexes.Where(i => !draws.Any(e => batch.Movements[i] is { } move &&
            move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(target) && move.Reason == e.Reason &&
            move.Sequence > e.SequenceBefore && move.Sequence <= e.SequenceAfter)).ToArray();
    }

    private int? OwnTurnEnemyHandGainTarget(CardMovementBatchContext batch, int owner, int threshold)
    {
        if (batch.TurnNumber <= 0 || batch.TurnNumber != _turnNumber || batch.MovementTiming is not { } timing ||
            timing.Phase is TurnPhase.NotStarted or TurnPhase.Finished || !IsValidPlayerSeat(timing.ActualTurnOwnerSeat) ||
            !ActualHandGainTurnOpen(timing.ActualTurnOwnerSeat) || !IsValidPlayerSeat(owner) || !_players[owner].IsAlive)
            return null;
        var target = timing.ActualTurnOwnerSeat;
        if (target == owner || !_players[target].IsAlive || !_players[owner].TeamRevealed || !_players[target].TeamRevealed ||
            !PublicTeamEnemy(owner, _players[target]) ||
            batch.DestinationCounts?.SingleOrDefault(item => item.Location == CardLocation.Hand(target)) is not { } count ||
            count.CountAfter <= threshold) return null;
        var indexes = OwnTurnEnemyHandGainIndexes(batch, target);
        return indexes.Length > 0 && indexes.All(index => _cardMovements.Contains(batch.Movements[index])) ? target : null;
    }

    private IEnumerable<ProgramTriggerCandidate> CollectOwnTurnEnemyHandGainCandidates(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.BindOwnTurnEnemyHandGain)) yield break;
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!IsOwnTurnEnemyHandGainTrigger(trigger) ||
                OwnTurnEnemyHandGainTarget(batch, owner.Seat, trigger.Effects[0].Amount) is not { } target) continue;
            var indexes = OwnTurnEnemyHandGainIndexes(batch, target);
            yield return candidate with { OccurrenceIndex = indexes[0] };
        }
    }

    private ProgramSkillWindowContext CreateOwnTurnEnemyHandGainContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate candidate)
    {
        var trigger = GetProgramTrigger(candidate);
        var target = OwnTurnEnemyHandGainTarget(parent.Batch, candidate.OwnerSeat, trigger.Effects[0].Amount)
            ?? throw new InvalidOperationException("An own-turn enemy hand-gain candidate lost its original real gain batch.");
        var indexes = OwnTurnEnemyHandGainIndexes(parent.Batch, target);
        var count = parent.Batch.DestinationCounts!.Single(item => item.Location == CardLocation.Hand(target));
        return new(SkillProgramTriggerWindow.CardsGained, parent.Id, candidate.OwnerSeat,
            SourceSeat: parent.Batch.OriginOwnerSeat, TargetSeat: target,
            OccurrenceIndex: candidate.OccurrenceIndex, MovementIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], parent.Batch.NativeDrawInvocation?.ActualCount ?? indexes.Length, count,
                SkillProgramTriggerWindow.CardsGained, parent.Batch.MovementTiming), MovementBatch: parent.Batch);
    }

    private bool CanRunOwnTurnEnemyHandGain(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        var trigger = GetProgramTrigger(candidate);
        if (!IsOwnTurnEnemyHandGainTrigger(trigger) || context.Window != SkillProgramTriggerWindow.CardsGained ||
            context.OwnerSeat != candidate.OwnerSeat || context.MovementBatch is not { } batch ||
            context.ParentFrameId != batch.Id || context.SourceSeat != batch.OriginOwnerSeat ||
            context.OccurrenceIndex != candidate.OccurrenceIndex || context.MovementIndex != candidate.OccurrenceIndex ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == batch.Id) is not CardsMovedTriggerWindowFrame parent ||
            parent.Batch != batch || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex] != candidate ||
            OwnTurnEnemyHandGainTarget(batch, candidate.OwnerSeat, trigger.Effects[0].Amount) is not { } target || context.TargetSeat != target)
            return false;
        return OwnTurnEnemyHandGainIndexes(batch, target)[0] == candidate.OccurrenceIndex;
    }

    private void BindOwnTurnEnemyHandGain(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var trigger = GetProgramTrigger(active);
        if (active.WindowContext is not { } context || active.InstructionIndex != 1 ||
            !CanRunOwnTurnEnemyHandGain(new(active.OwnerSeat, active.SkillId, active.TriggerId!, active.SkillInstanceId,
                active.GameplayHash, trigger.Priority, context.OccurrenceIndex), context))
            throw new InvalidOperationException("An own-turn enemy hand-gain binding requires its exact current movement candidate.");
        ReplaceRuntimeTop(active with { SelectedTargetSeats = Array.AsReadOnly(new[] { context.TargetSeat!.Value }) });
    }

    // The paid selection is proved by the original owning movement window.
    // Later damage observers may change life, hand size or teams; they must not
    // requalify the already-bound target or detach its native damage child.
    private bool HasExactOwnTurnEnemyHandGainSelection(ProgramSkillFrame frame)
    {
        if (frame.TriggerId is null || frame.InstructionIndex < 1 || frame.SelectedTargetSeats is not [var target] ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: { } batch } context ||
            context.OwnerSeat != frame.OwnerSeat || context.TargetSeat != target || context.ParentFrameId != batch.Id ||
            context.SourceSeat != batch.OriginOwnerSeat || batch.MovementTiming?.ActualTurnOwnerSeat != target ||
            batch.DestinationCounts?.SingleOrDefault(c => c.Location == CardLocation.Hand(target)) is not { } count ||
            _resolutionStack.SingleOrDefault(f => f.Id == batch.Id) is not CardsMovedTriggerWindowFrame parent || parent.Batch != batch ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count)
            return false;
        var trigger = GetProgramTrigger(frame);
        if (!IsOwnTurnEnemyHandGainTrigger(trigger) || count.CountAfter <= trigger.Effects[0].Amount) return false;
        var indexes = OwnTurnEnemyHandGainIndexes(batch, target);
        return indexes.Length > 0 && context.OccurrenceIndex == indexes[0] && context.MovementIndex == indexes[0] &&
            indexes.All(i => _cardMovements.Contains(batch.Movements[i])) && parent.Candidates[parent.CandidateIndex] ==
            new ProgramTriggerCandidate(frame.OwnerSeat, frame.SkillId, frame.TriggerId, frame.SkillInstanceId,
                frame.GameplayHash, trigger.Priority, indexes[0]);
    }

    // Gains caused by an actual damage observer still use the same exact gain
    // batch and native child damage, without borrowing another use's target.
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 5714)]
    private bool AllowsOwnTurnEnemyHandGainNestedDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || source is not null || nature is not null || amount != 1 || frame.AttackAttempt is not null ||
            frame.InstructionIndex != 2 || frame.SelectedTargetSeats is not [var selected] || selected != target ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || frame.WindowContext is not { } context || ActiveDying is not null)
            return false;
        return HasExactOwnTurnEnemyHandGainSelection(frame);
    }

    [ProgramCapability]
    private sealed class OwnTurnEnemyHandGainProgramCapability(GameEngine engine) : IOwnTurnEnemyHandGainProgramHost
    {
        public void BindOwnTurnEnemyHandGain(ProgramSkillFrame frame) => engine.BindOwnTurnEnemyHandGain(frame);
    }
}
