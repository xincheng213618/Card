namespace CardGame.Core;

public sealed partial class GameEngine
{
    // Admission is a proof of the actual recovery invoice and paused damage
    // instruction. An AfterHpRecovered label alone grants no nested attack.
    private bool TryDescribeRecoveryDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature, bool sourceLess,
        int? explicitSourceSeat, out RecoveryDamageOrigin? origin, out long? damageWindowId) =>
        NativeHealthRecovery.TryDescribeRecoveryDamage(frame, target, amount, sourceReference, nature,
            sourceLess, explicitSourceSeat, out origin, out damageWindowId);

    private bool ExactRecoveryProducerEdge(int hpIndex, IReadOnlyList<IGameEvent> history) =>
        NativeHealthRecovery.ExactRecoveryProducerEdge(hpIndex, history);

    private bool PausedRecoveryDamageMatches(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? sourceReference, DamageNature? nature) =>
        NativeHealthRecovery.PausedRecoveryDamageMatches(frame, target, amount, sourceReference, nature);

    private void AssertRecoveryDamageOrigin(ProgramSkillFrame frame) =>
        NativeHealthRecovery.AssertRecoveryDamageOrigin(frame);
}
