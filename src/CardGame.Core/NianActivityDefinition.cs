namespace CardGame.Core;

/// <summary>2016 OL Nian: initial form followed by a shuffled four-form round cycle.</summary>
public sealed record NianActivityDefinition(IReadOnlyList<string> FormGeneralIds, int SharedRevives = 3);

public sealed record NianFormChangedEvent(int RoundNumber, int BossSeat, string PreviousGeneralId,
    string GeneralId, int CycleNumber, int CyclePosition) : IGameEvent;

public sealed record NianFormCycleStartedEvent(int CycleNumber, IReadOnlyList<string> GeneralIds) : IGameEvent;
