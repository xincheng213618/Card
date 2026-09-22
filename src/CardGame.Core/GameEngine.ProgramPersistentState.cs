namespace CardGame.Core;

public sealed record ProgramBooleanStateChangedEvent(
    int OwnerSeat,
    string SkillId,
    string SkillInstanceId,
    string StateId,
    bool Value,
    SkillProgramStateVisibility Visibility) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly Dictionary<ProgramBooleanStateKey, bool> _programBooleanStates = [];

    private bool GetProgramBooleanState(ProgramSkillFrame frame, string stateId)
    {
        return GetProgramBooleanState(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId);
    }

    private bool GetProgramBooleanState(int ownerSeat, string skillId, string skillInstanceId, string stateId)
    {
        var definition = GetProgramBooleanStateDefinition(skillId, stateId);
        var key = new ProgramBooleanStateKey(ownerSeat, skillId, skillInstanceId, stateId);
        if (!_programBooleanStates.TryGetValue(key, out var value))
        {
            value = definition.InitialValue;
            _programBooleanStates.Add(key, value);
        }
        return value;
    }

    private void SetProgramBooleanState(ProgramSkillFrame frame, string stateId, bool value)
    {
        var definition = GetProgramBooleanStateDefinition(frame, stateId);
        var key = new ProgramBooleanStateKey(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId);
        var previous = GetProgramBooleanState(frame, stateId);
        if (previous == value) return;
        _programBooleanStates[key] = value;
        // Private state remains engine-only; public event/snapshot projections must not disclose its value.
        if (definition.Visibility == SkillProgramStateVisibility.Public)
            QueueGameEvent(new ProgramBooleanStateChangedEvent(
                frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId, stateId, value, definition.Visibility));
    }

    private SkillProgramBooleanStateDefinition GetProgramBooleanStateDefinition(
        ProgramSkillFrame frame,
        string stateId) => GetProgramBooleanStateDefinition(frame.SkillId, stateId);

    private SkillProgramBooleanStateDefinition GetProgramBooleanStateDefinition(string skillId, string stateId) =>
        _contentRegistry!.GetSkill(skillId).Program!.BooleanStates.SingleOrDefault(item => item.Id == stateId) ??
        throw new InvalidOperationException($"Program boolean state '{stateId}' is not declared.");

    private readonly record struct ProgramBooleanStateKey(
        int OwnerSeat,
        string SkillId,
        string SkillInstanceId,
        string StateId);
}
