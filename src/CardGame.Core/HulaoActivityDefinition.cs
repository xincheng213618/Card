namespace CardGame.Core;

/// <summary>The two-form 2022 cooperative Hulao rules, on a three-versus-one boss roster.</summary>
public sealed record HulaoActivityDefinition(int TransformationHp = 4, int ShuffleLimit = 2);

public enum HulaoTransformationCause { HealthThreshold, FirstShuffle }
public sealed record HulaoDeckShuffledEvent(int ShuffleNumber, int ActualTurnNumber) : IGameEvent;
public sealed record HulaoTurnInterruptedEvent(int ActualTurnNumber, int OwnerSeat,
    HulaoTransformationCause Cause) : IGameEvent;
public sealed record HulaoResolutionCancelledEvent(long FrameId, ResolutionFrameKind FrameKind,
    int ActualTurnNumber) : IGameEvent;
