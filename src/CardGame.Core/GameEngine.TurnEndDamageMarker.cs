namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : ITurnEndDamageMarkerProgramHost
    {
        public void AddEndingTurnDamageMarker(ProgramSkillFrame frame, PlayerMarkerKind marker) =>
            engine.AddProgramEndingTurnDamageMarker(frame, marker);
    }

    private void AddProgramEndingTurnDamageMarker(ProgramSkillFrame supplied, PlayerMarkerKind marker)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding,
                Facts: { TurnOwnerDamageDealtThisTurn: { } damage } facts } context ||
            frame.TriggerId is null || frame.ActivationId != frame.TriggerId || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            context.OwnerSeat != frame.OwnerSeat || frame.OwnerSeat != _currentSeat || damage < 0 ||
            index < 1 || _resolutionStack[index - 1] is not TurnEndingBoundaryFrame parent ||
            parent.Id != context.ParentFrameId || parent.OwnerSeat != frame.OwnerSeat ||
            parent.TurnNumber != _turnNumber || _turnNumber <= 0 ||
            context.SourceSeat != frame.OwnerSeat || context.TargetSeat != frame.OwnerSeat ||
            parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex] is not { Kind: TurnEndingBoundaryItemKind.Program, Candidate: { } candidate } ||
            !MountObserverCandidateMatches(frame, candidate) || context.OccurrenceIndex != candidate.OccurrenceIndex ||
            facts != (parent.Items[parent.ItemIndex].Facts ?? parent.Facts) ||
            _contentRegistry.GetSkill(frame.SkillId).Program is not { } program || program.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("Ending-turn damage marker lost its exact owner, ending parent, frozen facts or source.");

        var trigger = GetProgramTrigger(frame);
        TurnEndDamageMarkerContract.ValidateTrigger(frame.SkillId, trigger);
        if (trigger.Effects[0].Marker != marker ||
            !trigger.Condition.Evaluate(facts, frame.SkillId, frame.SkillInstanceId) ||
            !_players[frame.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.SkillId == frame.SkillId && fact.BindingId == frame.TriggerId &&
                fact.SkillInstanceId == frame.SkillInstanceId && fact.OwnerSeat == frame.OwnerSeat &&
                fact.Window == SkillProgramTriggerWindow.TurnEnding) != 1)
            throw new InvalidOperationException("Ending-turn damage marker lost its mandatory original binding or marker.");

        var source = new CardConversionSource(frame.SkillId, frame.TriggerId, frame.OwnerSeat, frame.SkillInstanceId);
        if (CompleteProgramEventHistory().OfType<ProgramEndingTurnDamageMarkerAddedEvent>()
            .Any(fact => fact.FrameId == frame.Id || fact.ParentFrameId == parent.Id && fact.Source == source))
            throw new InvalidOperationException("Ending-turn damage marker cannot settle its original boundary twice.");

        var owner = _players[frame.OwnerSeat];
        var countBefore = owner.Markers.GetValueOrDefault(marker);
        var attributed = owner.MarkerSourceCounts.Where(pair => pair.Key.Marker == marker).ToArray();
        if (attributed.Any(pair => pair.Value <= 0) || attributed.Sum(pair => pair.Value) != countBefore)
            throw new InvalidOperationException("Ending-turn damage marker source totals are inconsistent.");
        var countAfter = checked(countBefore + damage);
        if (damage > 0)
        {
            var key = (marker, frame.OwnerSeat);
            var sourceCount = checked(owner.MarkerSourceCounts.GetValueOrDefault(key) + damage);
            owner.MarkerSourceCounts[key] = sourceCount;
            owner.Markers[marker] = countAfter;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame.Id, owner.Seat, marker, damage,
                countAfter, owner.Seat, $"skill-program.{frame.SkillId}.{GetProgramBindingId(frame)}.marker"));
        }
        AdvanceEventRulesAndQueueFact(new ProgramEndingTurnDamageMarkerAddedEvent(frame.Id, source, frame.GameplayHash,
            parent.Id, parent.TurnNumber, marker, damage, countBefore, countAfter));
        AdvanceRulesAndPublishState();
    }
}
