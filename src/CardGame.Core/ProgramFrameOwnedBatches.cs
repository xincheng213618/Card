namespace CardGame.Core;

/// <summary>Frozen remaining targets of one repeated strategic damage instruction.</summary>
public sealed record ProgramStrategicDamageBatch(
    int InstructionIndex,
    IReadOnlyList<int> RemainingTargetSeats);

/// <summary>A reward owed after the owning program's deferred pile gain completes.</summary>
public sealed record ProgramDeferredProviderReward(int Seat, int Count);
