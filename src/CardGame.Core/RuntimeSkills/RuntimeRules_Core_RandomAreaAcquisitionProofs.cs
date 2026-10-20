namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RandomAreaAcquisitionProofs
{
    internal static bool SameRandomAreaStep(RandomAreaAcquisitionStepReceipt a, RandomAreaAcquisitionStepReceipt b) =>
        a.SeatIndex == b.SeatIndex && a.TargetSeat == b.TargetSeat && a.SelectedZone == b.SelectedZone && a.RandomBefore == b.RandomBefore && a.RandomAfter == b.RandomAfter &&
        a.MovementBatchId == b.MovementBatchId && a.SequenceBefore == b.SequenceBefore && a.SequenceAfter == b.SequenceAfter &&
        a.EligibleZones.SequenceEqual(b.EligibleZones) && a.Materials.SequenceEqual(b.Materials) && a.Movements.SequenceEqual(b.Movements);
}
