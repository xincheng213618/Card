namespace CardGame.Core;

public sealed record NativeCardRecoveryReceipt(long RecoveryFrameId, int SourceSeat, int TargetSeat,
    CardKind CardKind, int RequestedAmount, int HpBefore, int HpAfter);
public sealed record NativeCardRecoveryAppliedEvent(long CardUseFrameId, NativeCardRecoveryReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private NativeHealthRecoveryRuntime NativeHealthRecovery => GetRuntimeModule<NativeHealthRecoveryRuntime>();

    private void RecordNativeCardRecovery(long useId, long recoveryId, int source, int target,
        CardKind kind, int requested, int before, int after) =>
        NativeHealthRecovery.RecordNativeCardRecovery(useId, recoveryId, source, target, kind, requested, before, after);

    private bool TryGetNativeRescueRecovery(int hpIndex, IReadOnlyList<IGameEvent> history, out DyingFrame? dying) =>
        NativeHealthRecovery.TryGetNativeRescueRecovery(hpIndex, history, out dying);

    private bool NativeCardRecoveryReturnMatches(CardUseFrame use, HpChangedTriggerWindowFrame hp,
        IReadOnlyList<IGameEvent> history) => NativeHealthRecovery.NativeCardRecoveryReturnMatches(use, hp, history);

    private static bool NativeRecoveryMaterialGroupMatches(CardUseFrame use) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_NativeRecoveryDamage.NativeRecoveryMaterialGroupMatches(use);

    private bool ExactPaidNativeRecoveryUseEdge(ResolutionFrame parent, ResolutionFrame child, IReadOnlyList<IGameEvent> history) =>
        NativeHealthRecovery.ExactPaidNativeRecoveryUseEdge(parent, child, history);

    private bool ExactPaidNativeRecoveryReplacementEdge(ResolutionFrame parent, ResolutionFrame child,
        IReadOnlyList<IGameEvent> history) => NativeHealthRecovery.ExactPaidNativeRecoveryReplacementEdge(parent, child, history);

    // Hide only the original dying token while its exact paid rescue's recovery
    // counter owns a real attack. Inner victims retain normal dying/death flow.
    private bool IsOriginalDyingSuspendedByRecoveryDamage(DyingFrame dying) =>
        NativeHealthRecovery.IsOriginalDyingSuspendedByRecoveryDamage(dying);
}
