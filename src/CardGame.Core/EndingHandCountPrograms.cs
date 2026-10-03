namespace CardGame.Core;

public sealed record ProgramEndingHandCountHistory(int OwnerSeat, string SkillId,
    string SkillInstanceId, string StateId, int LastActualTurnNumber, int LastHandCount,
    IReadOnlyList<int> Counts);
public sealed record ProgramEndingHandCountRecordedEvent(int OwnerSeat, string SkillId,
    string SkillInstanceId, string StateId, int ActualTurnNumber, int HandCount, bool IsNew) : IGameEvent;
internal interface IEndingHandCountProgramHost
{
    void RecordEndHandCountAndGrantMarker(ProgramSkillFrame frame, string stateId, PlayerMarkerKind marker);
}
public sealed class RecordEndHandCountAndGrantMarkerHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecordEndHandCountAndGrantMarker;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        ((IEndingHandCountProgramHost)host).RecordEndHandCountAndGrantMarker(frame, effect.StateId!, effect.Marker!.Value);
        return SkillProgramStepOutcome.Continue;
    }
}
internal sealed class RecordEndHandCountAndGrantMarkerDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordEndHandCountAndGrantMarker;
    public override ISkillProgramEffectHandler Handler { get; } = new RecordEndHandCountAndGrantMarkerHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "marker", "amount", "condition");
        if (r.RequiredInt("amount") != 1) throw new InvalidOperationException("An ending hand-count observation grants exactly one marker.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"), marker: r.RequiredEnum<PlayerMarkerKind>("marker"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new RequireOwnTurnBoundary()];
}

public sealed partial class GameEngine
{
    private readonly Dictionary<(int Owner, string Skill, string Instance, string State), ProgramEndingHandCountHistory> _endingHandCountHistories = [];
    private sealed partial class ProgramSkillHost : IEndingHandCountProgramHost
    {
        public void RecordEndHandCountAndGrantMarker(ProgramSkillFrame frame, string stateId, PlayerMarkerKind marker) =>
            engine.RecordProgramEndingHandCount(frame, stateId, marker);
    }

    /// <summary>Trusted rule diagnostics, not a player projection. Returned nested collections are frozen.</summary>
    public IReadOnlyList<ProgramEndingHandCountHistory> GetProgramEndingHandCountHistoryDiagnostics() =>
        Array.AsReadOnly(_endingHandCountHistories.Values.OrderBy(s => s.OwnerSeat).ThenBy(s => s.SkillId, StringComparer.Ordinal)
            .ThenBy(s => s.SkillInstanceId, StringComparer.Ordinal).ThenBy(s => s.StateId, StringComparer.Ordinal)
            .Select(s => s with { Counts = Array.AsReadOnly(s.Counts.ToArray()) }).ToArray());

    private void RecordProgramEndingHandCount(ProgramSkillFrame frame, string stateId, PlayerMarkerKind marker)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.OwnerSeat != _currentSeat || frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            context.OwnerSeat != _currentSeat || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not TurnEndingBoundaryFrame parent || parent.Id != context.ParentFrameId ||
            parent.OwnerSeat != frame.OwnerSeat || parent.TurnNumber != _turnNumber || _turnNumber <= 0 ||
            parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate ||
            candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.BindingId != frame.TriggerId || candidate.SkillInstanceId != frame.SkillInstanceId ||
            candidate.GameplayHash != frame.GameplayHash)
            throw new InvalidOperationException("An ending hand-count history requires its actual owner ending frame.");
        var key = (frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId);
        var count = GetHand(_players[frame.OwnerSeat]).Count;
        _endingHandCountHistories.TryGetValue(key, out var previous);
        if (previous is not null && previous.LastActualTurnNumber == _turnNumber) return;
        if (previous is not null && previous.LastActualTurnNumber > _turnNumber)
            throw new InvalidOperationException("Ending hand-count history cannot move its actual-turn cursor backwards.");
        var isNew = previous?.Counts.Contains(count) != true;
        var counts = Array.AsReadOnly((previous?.Counts ?? []).Append(count).Distinct().Order().ToArray());
        _endingHandCountHistories[key] = new(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId, _turnNumber, count, counts);
        if (isNew)
        {
            var owner = _players[frame.OwnerSeat];
            var source = (marker, frame.OwnerSeat);
            owner.MarkerSourceCounts[source] = checked(owner.MarkerSourceCounts.GetValueOrDefault(source) + 1);
            var total = checked(owner.Markers.GetValueOrDefault(marker) + 1);
            owner.Markers[marker] = total;
            AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(frame.Id, owner.Seat, marker, 1,
                total, owner.Seat, $"skill-program.{frame.SkillId}.{GetProgramBindingId(frame)}.marker"));
        }
        AdvanceEventRulesAndQueueFact(new ProgramEndingHandCountRecordedEvent(frame.OwnerSeat, frame.SkillId,
            frame.SkillInstanceId, stateId, _turnNumber, count, isNew));
        AdvanceRulesAndPublishState();
    }
}
